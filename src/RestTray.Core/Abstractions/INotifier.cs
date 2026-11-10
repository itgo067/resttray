namespace RestTray.Core.Abstractions;

/// <summary>
/// 系统通知抽象。Windows 用 Toast,Linux 用 libnotify(notify-send)。
/// </summary>
public interface INotifier
{
    /// <summary>弹出一条系统通知。</summary>
    void Show(string title, string message);
}
