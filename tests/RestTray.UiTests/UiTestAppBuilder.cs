using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RestTray;

// Avalonia.Headless.XUnit 通过该特性找到 AppBuilder,为每个 [AvaloniaFact] 准备 headless UI 线程
[assembly: AvaloniaTestApplication(typeof(RestTray.UiTests.UiTestAppBuilder))]

namespace RestTray.UiTests;

/// <summary>
/// Headless 测试用的 AppBuilder:与正式入口共享 AtomUI 主题/语言/字体配置,
/// 仅把平台窗口化(UseAtomUIPlatformDetect 内部为 UsePlatformDetect,无显示环境会失败)
/// 替换为 UseHeadless + UseSkia。UseHeadlessDrawing = false 表示走真实 Skia 渲染管线,
/// 这样 CaptureRenderedFrame 才能拿到真实像素而不是占位空图。
/// </summary>
public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            })
            .ConfigureAtomUI();
    }
}
