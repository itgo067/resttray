using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// 按平台与会话类型选择空闲检测实现,降级链的每一步都写日志:
/// Windows → GetLastInputInfo;
/// Linux Wayland 会话(WAYLAND_DISPLAY 有值)→ D-Bus GNOME IdleMonitor → D-Bus freedesktop ScreenSaver
///   → Wayland ext-idle-notify → XScreenSaver(libXss)→ xprintidle → "始终活跃";
/// Linux/macOS 纯 X11 会话 → XScreenSaver(libXss)→ xprintidle → "始终活跃"。
///
/// Wayland 说明:GNOME/Cinnamon 走 org.gnome.Mutter.IdleMonitor;freedesktop 标准接口在
/// KDE Plasma(Wayland)上虽注册但调用返回 NotSupported,探测即被淘汰;
/// KDE/wlroots 最终由 ext-idle-notify-v1(libwayland P/Invoke)命中。
/// </summary>
public static class IdleTimeSourceFactory
{
    /// <summary>一个命名探测项:返回 null 表示该方案在当前环境不可用。</summary>
    internal sealed record Probe(string Name, Func<IIdleTimeSource?> TryCreate);

    public static IIdleTimeSource Create() => Create(Environment.GetEnvironmentVariable, probesOverride: null);

    /// <summary>可注入环境变量与探测链,便于单测验证降级路径。</summary>
    internal static IIdleTimeSource Create(Func<string, string?> getEnv, IReadOnlyList<Probe>? probesOverride)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            SimpleLog.Info("空闲检测:Windows GetLastInputInfo");
            return new WindowsIdleTimeSource();
        }
#endif
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var probes = probesOverride ?? BuildLinuxProbes(getEnv);
            foreach (var probe in probes)
            {
                var source = probe.TryCreate();
                if (source is not null)
                {
                    SimpleLog.Info($"空闲检测:{source.Name}");
                    return source;
                }
                SimpleLog.Info($"空闲检测:{probe.Name} 不可用,尝试下一方案。");
            }

            SimpleLog.Warn("所有空闲检测方案均不可用(D-Bus/Wayland/X11 探测全部失败),"
                           + "空闲检测降级为\"始终活跃\":"
                           + "提醒将按人在电脑前处理,人不在时也会照常提醒。");
            return new AlwaysActiveIdleTimeSource();
        }

        SimpleLog.Warn($"未支持的平台({Environment.OSVersion}),空闲检测降级为\"始终活跃\"。");
        return new AlwaysActiveIdleTimeSource();
    }

    /// <summary>按会话类型(Wayland / 纯 X11)组装探测链,顺序即降级优先级。</summary>
    internal static IReadOnlyList<Probe> BuildLinuxProbes(Func<string, string?> getEnv)
    {
        var probes = new List<Probe>();
        var isWayland = !string.IsNullOrEmpty(getEnv("WAYLAND_DISPLAY"));
        if (isWayland)
        {
            probes.Add(new Probe("D-Bus GNOME IdleMonitor", () => MutterIdleMonitorIdleTimeSource.TryCreate()));
            probes.Add(new Probe("D-Bus freedesktop ScreenSaver", () => FreedesktopScreenSaverIdleTimeSource.TryCreate()));
            probes.Add(new Probe("Wayland ext-idle-notify", () => WaylandExtIdleTimeSource.TryCreate()));
        }

        probes.Add(new Probe("X11 XScreenSaver", () => XScreenSaverIdleTimeSource.TryCreate()));
        probes.Add(new Probe("xprintidle",
            () => XprintidleIdleTimeSource.IsAvailable() ? new XprintidleIdleTimeSource() : null));
        return probes;
    }
}
