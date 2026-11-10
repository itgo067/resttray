namespace RestTray.Core.Abstractions;

/// <summary>
/// 时钟抽象。生产环境使用 <see cref="SystemClock"/>,单元测试注入虚拟时钟,
/// 使状态机测试不依赖真实等待。
/// </summary>
public interface IClock
{
    /// <summary>当前本地时间(睡觉提醒按本地时间判断)。</summary>
    DateTime Now { get; }
}

/// <summary>真实系统时钟。</summary>
public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
