using System.Text.Json.Serialization;

namespace RestTray.Core.Config;

/// <summary>
/// 提醒配置。序列化为 JSON 存放在标准配置目录,字段均有默认值,
/// 首次运行自动生成,用户可手工编辑后重启生效。
/// </summary>
public sealed class ReminderConfig
{
    /// <summary>休息提醒间隔(分钟):累计活跃时长达到该值即提醒休息。</summary>
    public int RestIntervalMinutes { get; set; } = 45;

    /// <summary>空闲阈值(分钟):空闲超过该值视为"离开",活跃计时暂停(不清零)。</summary>
    public int IdleThresholdMinutes { get; set; } = 5;

    /// <summary>长休息阈值(分钟):空闲超过该值视为"已经休息过了",活跃计时清零。</summary>
    public int LongBreakMinutes { get; set; } = 10;

    /// <summary>睡觉提醒时间,格式 "HH:mm",按本地时间判断。</summary>
    public string SleepTime { get; set; } = "23:30";

    /// <summary>睡觉提醒升级间隔(分钟):用户不停手,每隔该时间再提醒一次。</summary>
    public int SleepEscalationIntervalMinutes { get; set; } = 15;

    /// <summary>睡觉提醒最大追加次数(首次提醒之外的升级次数)。</summary>
    public int SleepMaxExtraReminders { get; set; } = 3;

    /// <summary>引擎轮询间隔(秒)。</summary>
    public int TickIntervalSeconds { get; set; } = 5;

    [JsonIgnore]
    public TimeSpan RestInterval => TimeSpan.FromMinutes(Math.Max(1, RestIntervalMinutes));

    [JsonIgnore]
    public TimeSpan IdleThreshold => TimeSpan.FromMinutes(Math.Max(0, IdleThresholdMinutes));

    [JsonIgnore]
    public TimeSpan LongBreak => TimeSpan.FromMinutes(Math.Max(LongBreakMinutes, IdleThresholdMinutes));

    [JsonIgnore]
    public TimeSpan SleepEscalationInterval => TimeSpan.FromMinutes(Math.Max(1, SleepEscalationIntervalMinutes));

    /// <summary>解析睡觉时间,配置写坏时回退到 23:30。</summary>
    [JsonIgnore]
    public TimeOnly SleepTimeOfDay =>
        TimeOnly.TryParse(SleepTime, out var t) ? t : new TimeOnly(23, 30);
}
