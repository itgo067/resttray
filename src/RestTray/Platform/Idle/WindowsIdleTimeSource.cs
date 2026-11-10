#if WINDOWS
// 本文件仅在 net10.0-windows 目标下编译,Linux 构建完全隔离 Windows API
using System.Runtime.InteropServices;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// Windows 空闲检测:P/Invoke user32!GetLastInputInfo,
/// 返回距最后一次键鼠输入的时长。
/// </summary>
internal sealed class WindowsIdleTimeSource : IIdleTimeSource
{
    public string Name => "Windows GetLastInputInfo";

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero; // 查询失败按活跃处理,宁多提醒不漏提醒

        // Environment.TickCount 与 dwTime 同基准;用无符号减法天然处理 49.7 天回绕
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
#endif
