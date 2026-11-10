#if WINDOWS
// 本文件仅在 net10.0-windows 目标下编译
using Microsoft.Toolkit.Uwp.Notifications;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Notify;

/// <summary>
/// Windows 通知:WinRT Toast(Windows 10/11 操作中心)。
/// </summary>
internal sealed class WindowsToastNotifier : INotifier
{
    public void Show(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Toast 通知发送失败", ex);
        }
    }
}
#endif
