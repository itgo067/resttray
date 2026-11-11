using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia;

namespace RestTray;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Platform.SimpleLog.Error("应用启动失败", ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            // 平台窗口化(X11/Win32/Wayland 自动检测);headless 测试不走这里
            .UseAtomUIPlatformDetect()
            .ConfigureAtomUI()
            .LogToTrace();
    }
}
