using System.Runtime.InteropServices;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// Linux/X11 空闲检测:P/Invoke libXss(XScreenSaver 扩展)的 XScreenSaverQueryInfo。
/// 这是 X11 下标准的空闲时间来源,无需 fork 外部进程。
/// 纯 Wayland 环境(无 XWayland)下库或 DISPLAY 不可用,由工厂负责降级。
/// </summary>
internal sealed class XScreenSaverIdleTimeSource : IIdleTimeSource
{
    private readonly IntPtr _display;
    private readonly IntPtr _rootWindow;

    public string Name => "X11 XScreenSaver";

    private XScreenSaverIdleTimeSource(IntPtr display, IntPtr rootWindow)
    {
        _display = display;
        _rootWindow = rootWindow;
    }

    /// <summary>尝试创建实例;库缺失或 X 服务器不可达时返回 null(由工厂继续降级)。</summary>
    public static XScreenSaverIdleTimeSource? TryCreate()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;

        try
        {
            var display = Native.XOpenDisplay(null);
            if (display == IntPtr.Zero)
                return null;

            if (!Native.XScreenSaverQueryExtension(display, out _, out _))
            {
                Native.XCloseDisplay(display);
                return null;
            }

            var root = Native.XRootWindow(display, Native.XDefaultScreen(display));
            if (!Native.XScreenSaverQueryInfo(display, root, out _))
            {
                Native.XCloseDisplay(display);
                return null;
            }

            return new XScreenSaverIdleTimeSource(display, root);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or BadImageFormatException or TypeLoadException)
        {
            return null;
        }
    }

    public TimeSpan GetIdleTime()
    {
        if (!Native.XScreenSaverQueryInfo(_display, _rootWindow, out var info))
            return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds((double)info.Idle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public IntPtr Window;
        public int State;
        public int Kind;
        public nuint TilOrSince;
        public nuint Idle;      // 空闲毫秒数
        public nuint EventMask;
    }

    private static class Native
    {
        [DllImport("libX11.so.6")]
        internal static extern IntPtr XOpenDisplay(string? displayName);

        [DllImport("libX11.so.6")]
        internal static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern int XDefaultScreen(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern IntPtr XRootWindow(IntPtr display, int screenNumber);

        [DllImport("libXss.so.1")]
        internal static extern bool XScreenSaverQueryExtension(IntPtr display, out int eventBase, out int errorBase);

        [DllImport("libXss.so.1")]
        internal static extern bool XScreenSaverQueryInfo(IntPtr display, IntPtr drawable, out XScreenSaverInfo info);
    }
}
