using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RestTray.Core.Config;
using RestTray.Settings;

namespace RestTray.UiTests;

/// <summary>
/// SettingsWindow 的 headless 渲染截图测试。
/// 截图统一输出到仓库 docs/screenshots/(随 git 提交,README 引用),
/// 同时断言位图非空白,保证测试本身有验证价值而非只产图。
/// </summary>
public class SettingsWindowScreenshotTests
{
    [AvaloniaFact]
    public void SettingsWindow_DefaultConfig_RendersNonBlank()
    {
        var window = new SettingsWindow(new ReminderConfig());

        var (bitmap, path) = Capture(window, "settings-window-default.png");

        AssertNonBlank(bitmap, path);
        bitmap.Dispose();
    }

    [AvaloniaFact]
    public void SettingsWindow_CustomConfig_RendersNonBlank()
    {
        var config = new ReminderConfig
        {
            RestIntervalMinutes = 90,
            IdleThresholdMinutes = 3,
            LongBreakMinutes = 20,
            SleepTime = "22:15",
            SleepEscalationIntervalMinutes = 30,
        };
        var window = new SettingsWindow(config);

        var (bitmap, path) = Capture(window, "settings-window-custom.png");

        AssertNonBlank(bitmap, path);
        bitmap.Dispose();
    }

    /// <summary>显示窗口、驱动一轮真实渲染并保存 PNG,返回位图与输出路径。</summary>
    private static (Bitmap Bitmap, string Path) Capture(
        Avalonia.Controls.Window window, string fileName)
    {
        window.Show();
        // 让布局/动画/异步绑定全部跑完再截图
        Dispatcher.UIThread.RunJobs();

        var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);

        var dir = ScreenshotsDir();
        var path = Path.Combine(dir, fileName);
        bitmap.Save(path, new PngBitmapEncoderOptions());

        window.Close();
        return (bitmap, path);
    }

    /// <summary>
    /// 断言截图有验证价值:文件已落盘、尺寸足够看清内容、像素不全为同一颜色。
    /// </summary>
    private static void AssertNonBlank(Bitmap bitmap, string path)
    {
        Assert.True(File.Exists(path), $"截图文件未生成:{path}");
        Assert.True(new FileInfo(path).Length > 0, $"截图文件为空:{path}");

        var size = bitmap.PixelSize;
        Assert.True(size.Width >= 400, $"截图宽度异常:{size.Width}");
        Assert.True(size.Height >= 200, $"截图高度异常:{size.Height}");

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // 与左上角第一个像素逐一比较;窗口有标题栏、文字、输入框,绝不可能纯色
        var r0 = pixels[0];
        var g0 = pixels[1];
        var b0 = pixels[2];
        var a0 = pixels[3];
        var different = 0;
        for (var i = 4; i < pixels.Length; i += 4)
        {
            if (pixels[i] != r0 || pixels[i + 1] != g0 || pixels[i + 2] != b0 || pixels[i + 3] != a0)
            {
                different++;
            }
        }

        Assert.True(different > pixels.Length / 4 / 10,
            $"截图疑似空白:超过 90% 像素与首像素相同({path})");
    }

    /// <summary>从测试程序集目录向上找到仓库根(含 RestTray.slnx),返回 docs/screenshots。</summary>
    private static string ScreenshotsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestTray.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir); // 找不到仓库根视为测试环境错误
        var screenshots = Path.Combine(dir.FullName, "docs", "screenshots");
        Directory.CreateDirectory(screenshots);
        return screenshots;
    }
}
