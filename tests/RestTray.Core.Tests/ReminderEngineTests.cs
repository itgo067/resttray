using RestTray.Core.Config;
using RestTray.Core.Engine;

namespace RestTray.Core.Tests;

/// <summary>
/// 状态机单元测试:全部通过虚拟时钟 + 虚拟空闲源驱动。
/// 默认配置:休息间隔 45 分钟,空闲阈值 5 分钟,长休息 10 分钟,睡觉 23:30,升级间隔 15 分钟、最多 3 次。
/// </summary>
public class ReminderEngineTests
{
    private static readonly DateTime Morning = new(2026, 1, 12, 9, 0, 0); // 周一上午

    private sealed class Fixture
    {
        public FakeClock Clock = new(Morning);
        public FakeIdleSource Idle = new();
        public ReminderConfig Config = new();
        public ReminderEngine Engine;
        public int RestCount;
        public List<int> SleepLevels = new();

        public Fixture()
        {
            Engine = new ReminderEngine(Clock, Idle, Config);
            Engine.RestReminderDue += () => RestCount++;
            Engine.SleepReminderDue += level => SleepLevels.Add(level);
        }

        /// <summary>推进一段时间,保持每 5 秒一次 Tick(模拟宿主营询)。</summary>
        public void Advance(TimeSpan total, TimeSpan? idleDuring = null)
        {
            if (idleDuring is { } idle) Idle.Idle = idle;
            var step = TimeSpan.FromSeconds(5);
            for (var t = TimeSpan.Zero; t < total; t += step)
            {
                Clock.Advance(step);
                Engine.Tick();
            }
        }
    }

    // ---- 休息提醒 ----

    [Fact]
    public void 持续活跃达到间隔后触发休息提醒并清零()
    {
        var f = new Fixture();
        f.Engine.Tick(); // 首个 Tick 只建立基准

        f.Advance(TimeSpan.FromMinutes(44));
        Assert.Equal(0, f.RestCount);

        f.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, f.RestCount);
        Assert.True(f.Engine.ActiveAccumulated < TimeSpan.FromMinutes(1)); // 触发后清零重新计
    }

    [Fact]
    public void 短暂离开_空闲介于阈值之间_计时冻结不清零()
    {
        var f = new Fixture();
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(30)); // 活跃 30 分钟

        // 空闲 6 分钟(5 < 6 < 10):冻结
        f.Advance(TimeSpan.FromMinutes(6), idleDuring: TimeSpan.FromMinutes(6));
        Assert.Equal(TimeSpan.FromMinutes(30), f.Engine.ActiveAccumulated);

        // 回来继续活跃 15 分钟 → 累计 45 触发
        f.Idle.Idle = TimeSpan.Zero;
        f.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(1, f.RestCount);
    }

    [Fact]
    public void 长休息后计时清零()
    {
        var f = new Fixture();
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(40));

        // 离开 12 分钟(>= 长休息阈值)→ 视为已休息
        f.Advance(TimeSpan.FromMinutes(12), idleDuring: TimeSpan.FromMinutes(12));
        Assert.Equal(TimeSpan.Zero, f.Engine.ActiveAccumulated);

        // 重新活跃 44 分钟仍不触发(需要完整 45)
        f.Idle.Idle = TimeSpan.Zero;
        f.Advance(TimeSpan.FromMinutes(44));
        Assert.Equal(0, f.RestCount);
    }

    // ---- 睡觉提醒 ----

    [Fact]
    public void 睡觉时间到点且人在活跃时触发首次提醒()
    {
        var f = new Fixture();
        f.Clock.Now = new DateTime(2026, 1, 12, 23, 29, 0);
        f.Engine.Tick();

        f.Advance(TimeSpan.FromMinutes(2)); // 跨过 23:30,全程活跃
        Assert.Equal([0], f.SleepLevels);
    }

    [Fact]
    public void 睡觉时间到点但人不活跃时当晚跳过()
    {
        var f = new Fixture();
        f.Clock.Now = new DateTime(2026, 1, 12, 23, 25, 0);
        f.Engine.Tick();

        // 人离开 8 分钟(超过空闲阈值但不足长休息)
        f.Advance(TimeSpan.FromMinutes(8), idleDuring: TimeSpan.FromMinutes(8));
        Assert.Empty(f.SleepLevels);

        // 之后回来继续用,当晚也不应再触发(已在不活跃时处理过)
        f.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(f.SleepLevels);
    }

    [Fact]
    public void 睡觉提醒在用户不停手时每15分钟升级一次最多再提醒3次()
    {
        var f = new Fixture();
        f.Clock.Now = new DateTime(2026, 1, 12, 23, 29, 0);
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(2)); // 23:31 → 首次提醒(level 0)

        f.Advance(TimeSpan.FromMinutes(15)); // level 1
        f.Advance(TimeSpan.FromMinutes(15)); // level 2
        f.Advance(TimeSpan.FromMinutes(15)); // level 3
        Assert.Equal([0, 1, 2, 3], f.SleepLevels);

        f.Advance(TimeSpan.FromMinutes(30)); // 已达上限,不再提醒
        Assert.Equal(4, f.SleepLevels.Count);
    }

    [Fact]
    public void 催睡过程中人离开则停止升级()
    {
        var f = new Fixture();
        f.Clock.Now = new DateTime(2026, 1, 12, 23, 29, 0);
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(2)); // 首次提醒

        // 人离开超过长休息阈值 → 催睡取消
        f.Advance(TimeSpan.FromMinutes(11), idleDuring: TimeSpan.FromMinutes(11));
        // 即使再回来,当晚也不再催(已处理过该日期)
        f.Advance(TimeSpan.FromMinutes(40));
        Assert.Equal([0], f.SleepLevels);
    }

    // ---- 暂停/恢复 ----

    [Fact]
    public void 暂停期间不计时不提醒_恢复后重新累计()
    {
        var f = new Fixture();
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(40));

        f.Engine.Pause(TimeSpan.FromHours(1));
        Assert.True(f.Engine.IsPaused);
        Assert.Equal(TimeSpan.Zero, f.Engine.ActiveAccumulated);

        // 暂停 1 小时内一直活跃也不提醒;且跨过睡觉时间也被静默
        f.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal(0, f.RestCount);
        Assert.Equal(TimeSpan.Zero, f.Engine.ActiveAccumulated);

        // 暂停自然到期后继续计时
        f.Advance(TimeSpan.FromMinutes(11)); // 累计 61 分钟,暂停已结束,活跃累计 ~1 分钟
        Assert.False(f.Engine.IsPaused);
        Assert.True(f.Engine.ActiveAccumulated > TimeSpan.Zero);

        // 手动恢复场景
        f.Engine.Pause(TimeSpan.FromHours(1));
        f.Engine.Resume();
        f.Advance(TimeSpan.FromMinutes(45));
        Assert.Equal(1, f.RestCount);
    }

    // ---- 状态展示 ----

    [Fact]
    public void 距下次休息剩余时间计算正确()
    {
        var f = new Fixture();
        f.Engine.Tick();
        f.Advance(TimeSpan.FromMinutes(20));

        var remaining = f.Engine.TimeToNextRest;
        Assert.Equal(TimeSpan.FromMinutes(25), remaining);
    }
}
