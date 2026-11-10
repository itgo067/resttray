using RestTray.Core.Abstractions;
using RestTray.Core.Config;

namespace RestTray.Core.Engine;

/// <summary>
/// 提醒状态机(核心逻辑,与 UI/平台完全解耦)。
/// 核心理念:所有提醒都以"人在电脑前活动"为前提——人在才提醒,不在就跳过/顺延。
///
/// 由宿主周期调用 <see cref="Tick"/>(建议每 5 秒一次)。时钟与空闲来源均通过注入提供,
/// 单元测试可用虚拟时钟驱动,不依赖真实等待。
/// </summary>
public sealed class ReminderEngine
{
    private readonly IClock _clock;
    private readonly IIdleTimeSource _idleSource;
    private readonly ReminderConfig _config;

    private bool _firstTick = true;
    private DateTime _lastTick;

    /// <summary>当前累计活跃时长(有键鼠输入才累计)。</summary>
    public TimeSpan ActiveAccumulated { get; private set; } = TimeSpan.Zero;

    // ---- 睡觉提醒状态 ----
    private DateOnly? _sleepHandledDate;   // 今晚是否已处理过(提醒或跳过)
    private bool _sleepReminding;          // 是否处于"持续催睡"阶段
    private int _sleepEscalations;         // 已升级次数
    private DateTime _nextEscalationAt;    // 下次升级时间点

    private DateTime? _pausedUntil;

    /// <summary>触发休息提醒(累计活跃达到间隔时,触发后计时清零重新开始)。</summary>
    public event Action? RestReminderDue;

    /// <summary>
    /// 触发睡觉提醒。参数为提醒级别:0 = 首次到点提醒,1..N = 用户不停手时的升级提醒。
    /// </summary>
    public event Action<int>? SleepReminderDue;

    public ReminderEngine(IClock clock, IIdleTimeSource idleSource, ReminderConfig config)
    {
        _clock = clock;
        _idleSource = idleSource;
        _config = config;
    }

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => _pausedUntil is { } until && _clock.Now < until;

    /// <summary>暂停截止时间(未暂停为 null)。</summary>
    public DateTime? PausedUntil => IsPaused ? _pausedUntil : null;

    /// <summary>距下次休息提醒的剩余时间(已归零则返回 Zero)。</summary>
    public TimeSpan TimeToNextRest
    {
        get
        {
            var remaining = _config.RestInterval - ActiveAccumulated;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>暂停提醒一段时间(暂停期间不计时、不提醒,累计活跃清零,相当于放自己一马)。</summary>
    public void Pause(TimeSpan duration)
    {
        _pausedUntil = _clock.Now + duration;
        ActiveAccumulated = TimeSpan.Zero;
        CancelSleepReminding();
    }

    /// <summary>立即恢复提醒。</summary>
    public void Resume()
    {
        _pausedUntil = null;
        _lastTick = _clock.Now;
    }

    /// <summary>推进一次状态机。宿主按配置的轮询间隔调用。</summary>
    public void Tick()
    {
        var now = _clock.Now;
        if (_firstTick)
        {
            _lastTick = now;
            _firstTick = false;
            return;
        }

        var elapsed = now - _lastTick;
        _lastTick = now;
        if (elapsed <= TimeSpan.Zero)
            return; // 时钟回拨保护

        if (IsPaused)
            return; // 暂停期间完全静默

        var idle = SafeGetIdle();

        if (idle >= _config.LongBreak)
        {
            // 长时间无输入:视为已经休息过了,计时清零;若在催睡,人也已经离开,取消催睡
            ActiveAccumulated = TimeSpan.Zero;
            if (_sleepReminding)
                CancelSleepReminding();
        }
        else if (idle < _config.IdleThreshold)
        {
            // 人在活跃操作:累计活跃时间
            ActiveAccumulated += elapsed;
        }
        // 介于两者之间:短暂离开(如看视频没动鼠标),计时冻结——不清零也不累计

        if (ActiveAccumulated >= _config.RestInterval)
        {
            ActiveAccumulated = TimeSpan.Zero;
            RestReminderDue?.Invoke();
        }

        HandleSleepReminder(now, idle);
    }

    private void HandleSleepReminder(DateTime now, TimeSpan idle)
    {
        var today = DateOnly.FromDateTime(now);
        var sleepTime = _config.SleepTimeOfDay;

        if (_sleepReminding)
        {
            if (idle >= _config.LongBreak)
            {
                // 人走了(应该是去睡了),停止催睡
                CancelSleepReminding();
                return;
            }

            if (now >= _nextEscalationAt)
            {
                if (idle < _config.IdleThreshold)
                {
                    if (_sleepEscalations < _config.SleepMaxExtraReminders)
                    {
                        _sleepEscalations++;
                        _nextEscalationAt = now + _config.SleepEscalationInterval;
                        SleepReminderDue?.Invoke(_sleepEscalations);
                    }
                    else
                    {
                        // 已升级到上限,今晚不再打扰
                        _sleepReminding = false;
                    }
                }
                else
                {
                    // 短暂停顿(介于阈值之间):暂不升级,一分钟后复查
                    _nextEscalationAt = now + TimeSpan.FromMinutes(1);
                }
            }
            return;
        }

        // 到点且今晚未处理过
        if (now.TimeOfDay >= sleepTime.ToTimeSpan() && _sleepHandledDate != today)
        {
            _sleepHandledDate = today;

            if (idle < _config.IdleThreshold)
            {
                // 人在活跃:开始催睡
                _sleepReminding = true;
                _sleepEscalations = 0;
                _nextEscalationAt = now + _config.SleepEscalationInterval;
                SleepReminderDue?.Invoke(0);
            }
            // 人不活跃:视为已经离开/已睡,当晚跳过——这正是"不在就不提醒"
        }
    }

    private void CancelSleepReminding()
    {
        _sleepReminding = false;
        _sleepEscalations = 0;
    }

    private TimeSpan SafeGetIdle()
    {
        try
        {
            var idle = _idleSource.GetIdleTime();
            return idle < TimeSpan.Zero ? TimeSpan.Zero : idle;
        }
        catch (Exception)
        {
            // 空闲检测失败时按"正在活跃"处理,宁可多提醒也不错过后无法恢复
            return TimeSpan.Zero;
        }
    }
}
