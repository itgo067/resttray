namespace RestTray.Core.Abstractions;

/// <summary>
/// 系统空闲时间来源(距离上一次键鼠输入的时长)。
/// 各平台由 UI 层实现:Windows 用 GetLastInputInfo,Linux 用 XScreenSaver/xprintidle。
/// </summary>
public interface IIdleTimeSource
{
    /// <summary>来源名称,用于日志和诊断。</summary>
    string Name { get; }

    /// <summary>获取当前系统空闲时长。实现必须快速、不抛异常(异常时按空闲 0 处理)。</summary>
    TimeSpan GetIdleTime();
}
