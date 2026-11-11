using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using AtomUI.Theme;
using Avalonia;
using ReactiveUI.Avalonia;

namespace RestTray;

/// <summary>
/// AppBuilder 上平台无关部分的共享配置(ReactiveUI + AtomUI 主题/语言/字体/控件)。
/// 平台窗口化由调用方决定:正式入口用 UseAtomUIPlatformDetect,Headless UI 测试用 UseHeadless。
/// </summary>
public static class AppBuilderExtensions
{
    public static AppBuilder ConfigureAtomUI(this AppBuilder builder)
    {
        return builder
            .UseReactiveUI(_ => { })
            .WithAtomUIDefaultOptions()
            .UseAtomUI(atomUI =>
            {
                atomUI.UseLanguages(
                    LanguageTags.ZhCN,
                    [LanguageTags.ZhCN, LanguageTags.EnUS]);
                atomUI.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
                atomUI.UseAlibabaSansFont();
                atomUI.UseDesktopControls();
            });
    }
}
