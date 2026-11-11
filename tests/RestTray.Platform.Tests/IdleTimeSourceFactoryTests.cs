using RestTray.Core.Abstractions;
using RestTray.Platform.Idle;

namespace RestTray.Platform.Tests;

/// <summary>
/// 空闲检测工厂的降级链测试:探测项全部注入 fake,不依赖真实桌面环境,
/// 在 CI(无 session bus、无显示)上同样可运行。
/// </summary>
public class IdleTimeSourceFactoryTests
{
    [Fact]
    public void Wayland_chain_prefers_dbus_then_wayland_native_then_x11()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var probes = IdleTimeSourceFactory.BuildLinuxProbes(
            name => name == "WAYLAND_DISPLAY" ? "wayland-0" : null);

        Assert.Equal(
            new[]
            {
                "D-Bus GNOME IdleMonitor",
                "D-Bus freedesktop ScreenSaver",
                "Wayland ext-idle-notify",
                "X11 XScreenSaver",
                "xprintidle",
            },
            probes.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void X11_session_keeps_original_probe_order()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var probes = IdleTimeSourceFactory.BuildLinuxProbes(
            name => name == "DISPLAY" ? ":0" : null);

        Assert.Equal(
            new[] { "X11 XScreenSaver", "xprintidle" },
            probes.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void All_probes_fail_falls_back_to_always_active()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var probes = new IdleTimeSourceFactory.Probe[]
        {
            new("fake-dbus", () => null),
            new("fake-wayland", () => null),
            new("fake-x11", () => null),
        };

        var source = IdleTimeSourceFactory.Create(_ => null, probes);

        Assert.IsType<AlwaysActiveIdleTimeSource>(source);
    }

    [Fact]
    public void First_available_probe_wins_and_later_probes_are_not_tried()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var marker = new FakeIdleSource("fake-wayland");
        var laterProbed = false;
        var probes = new IdleTimeSourceFactory.Probe[]
        {
            new("fake-dbus", () => null),
            new("fake-wayland", () => marker),
            new("fake-x11", () =>
            {
                laterProbed = true;
                return new FakeIdleSource("fake-x11");
            }),
        };

        var source = IdleTimeSourceFactory.Create(_ => null, probes);

        Assert.Same(marker, source);
        Assert.False(laterProbed);
    }

    private sealed class FakeIdleSource : IIdleTimeSource
    {
        public FakeIdleSource(string name) => Name = name;

        public string Name { get; }

        public TimeSpan GetIdleTime() => TimeSpan.Zero;
    }
}
