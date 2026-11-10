using RestTray.Core.Abstractions;

namespace RestTray.Platform.Notify;

/// <summary>
/// 按平台选择通知实现:Windows → Toast;Linux → notify-send;都不可用时写日志兜底。
/// </summary>
public static class NotifierFactory
{
    public static INotifier Create()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            SimpleLog.Info("通知方式:Windows Toast");
            return new WindowsToastNotifier();
        }
#endif
        if (OperatingSystem.IsLinux() && NotifySendNotifier.IsAvailable())
        {
            SimpleLog.Info("通知方式:notify-send(libnotify)");
            return new NotifySendNotifier();
        }

        SimpleLog.Warn("未找到可用的系统通知渠道,通知将只写入日志。");
        return new LogOnlyNotifier();
    }

    /// <summary>兜底:通知写日志,不弹窗。</summary>
    private sealed class LogOnlyNotifier : INotifier
    {
        public void Show(string title, string message) => SimpleLog.Info($"[通知] {title} — {message}");
    }
}
