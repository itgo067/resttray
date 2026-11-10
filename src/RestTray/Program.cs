using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using AtomUI.Theme;
using Avalonia;
using ReactiveUI.Avalonia;

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
            .UseReactiveUI(_ => { })
            .UseAtomUIPlatformDetect()
            .WithAtomUIDefaultOptions()
            .UseAtomUI(builder =>
            {
                builder.UseLanguages(
                    LanguageTags.ZhCN,
                    [LanguageTags.ZhCN, LanguageTags.EnUS]);
                builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
                builder.UseAlibabaSansFont();
                builder.UseDesktopControls();
            })
            .LogToTrace();
    }
}
