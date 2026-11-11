using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia;

namespace RestTray;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 诊断参数:打印当前命中的空闲检测方案与空闲秒数,用于排查各桌面环境的探测链路。
        // --print-idle [观察秒数]:不给秒数时采样一次退出;给秒数时每 2 秒采样一次
        // (Wayland ext-idle 方案基于事件估算,启动后需要几秒才能反映真实空闲,观察模式可看到数值增长)。
        var printIdleIndex = Array.IndexOf(args, "--print-idle");
        if (printIdleIndex >= 0)
        {
            var watchSeconds = 0;
            if (printIdleIndex + 1 < args.Length)
                int.TryParse(args[printIdleIndex + 1], out watchSeconds);

            var source = Platform.Idle.IdleTimeSourceFactory.Create();
            Console.WriteLine($"空闲检测来源:{source.Name}");
            var deadline = DateTime.Now.AddSeconds(watchSeconds);
            while (true)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 当前空闲:{source.GetIdleTime().TotalSeconds:F1} 秒");
                if (DateTime.Now >= deadline)
                    break;
                Thread.Sleep(2000);
            }
            return 0;
        }

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
