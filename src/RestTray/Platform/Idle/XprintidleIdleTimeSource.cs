using System.Diagnostics;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// Linux/X11 空闲检测的备选方案:调用外部命令 xprintidle(输出空闲毫秒数)。
/// 当 libXss 不可用但系统安装了 xprintidle 时使用。
/// 每 5 秒 fork 一次极轻量进程,对个人工具可接受。
/// </summary>
internal sealed class XprintidleIdleTimeSource : IIdleTimeSource
{
    public string Name => "xprintidle";

    /// <summary>探测系统是否安装了 xprintidle 且能正常执行。</summary>
    public static bool IsAvailable()
    {
        try
        {
            return RunOnce() is not null;
        }
        catch
        {
            return false;
        }
    }

    public TimeSpan GetIdleTime()
    {
        var ms = RunOnce();
        return ms is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms.Value);
    }

    private static long? RunOnce()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "xprintidle",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
            return null;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(2000);
        return process.ExitCode == 0 && long.TryParse(output, out var ms) ? ms : null;
    }
}
