using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// 降级方案:无法检测空闲时按"始终活跃"处理(空闲恒为 0)。
/// 即假设人一直在电脑前——提醒照常进行,符合"宁可多提醒"的策略,
/// 已在工厂中记录警告日志告知用户。
/// </summary>
internal sealed class AlwaysActiveIdleTimeSource : IIdleTimeSource
{
    public string Name => "始终活跃(降级)";

    public TimeSpan GetIdleTime() => TimeSpan.Zero;
}
