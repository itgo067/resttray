using RestTray.Platform.Idle;

namespace RestTray.Platform.Tests;

/// <summary>
/// Wayland ext-idle 空闲估算的纯函数测试:协议只给 idled/resumed 事件,
/// 空闲起点按 idled 事件时刻回推阈值估算。
/// </summary>
public class WaylandExtIdleTimeSourceTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Not_idled_returns_zero()
    {
        Assert.Equal(TimeSpan.Zero,
            WaylandExtIdleTimeSource.ComputeIdle(alive: true, idled: false, Now, Now, Threshold));
    }

    [Fact]
    public void Connection_dead_returns_zero()
    {
        Assert.Equal(TimeSpan.Zero,
            WaylandExtIdleTimeSource.ComputeIdle(alive: false, idled: true,
                Now - TimeSpan.FromMinutes(3), Now, Threshold));
    }

    [Fact]
    public void Idled_time_is_elapsed_since_event_plus_threshold()
    {
        var idledAt = Now - TimeSpan.FromSeconds(115);
        var idle = WaylandExtIdleTimeSource.ComputeIdle(alive: true, idled: true, idledAt, Now, Threshold);
        Assert.Equal(TimeSpan.FromSeconds(120), idle);
    }
}
