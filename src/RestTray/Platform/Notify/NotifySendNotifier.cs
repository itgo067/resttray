using System.Diagnostics;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Notify;

/// <summary>
/// Linux 通知:调用 libnotify 的 notify-send 命令
/// (桌面环境几乎都提供,内部走 FreeDesktop Notifications D-Bus 协议)。
/// </summary>
internal sealed class NotifySendNotifier : INotifier
{
    /// <summary>探测 notify-send 是否可用。</summary>
    public static bool IsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(2000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    public void Show(string title, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                // -u critical:休息/睡觉提醒值得更醒目的呈现
                ArgumentList = { "-u", "critical", "-a", "RestTray", title, message },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            SimpleLog.Error("notify-send 调用失败", ex);
        }
    }
}
