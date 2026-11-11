using RestTray.Platform.Idle;

namespace RestTray.Platform.Tests;

/// <summary>
/// D-Bus 空闲源测试:查询委托注入验证换算与异常兜底;
/// TryCreate 注入无效 bus 地址验证无 session bus 环境(CI)下的优雅降级。
/// </summary>
public class DBusIdleTimeSourceTests
{
    private const string BogusBusAddress = "unix:path=/tmp/resttray-definitely-missing-bus.sock";

    [Fact]
    public void Mutter_converts_milliseconds_to_timespan()
    {
        var source = new MutterIdleMonitorIdleTimeSource(() => Task.FromResult<ulong>(123_456));
        Assert.Equal(TimeSpan.FromMilliseconds(123_456), source.GetIdleTime());
    }

    [Fact]
    public void Freedesktop_converts_seconds_to_timespan()
    {
        var source = new FreedesktopScreenSaverIdleTimeSource(() => Task.FromResult(7u));
        Assert.Equal(TimeSpan.FromSeconds(7), source.GetIdleTime());
    }

    [Fact]
    public void Query_exception_is_swallowed_and_returns_zero()
    {
        var mutter = new MutterIdleMonitorIdleTimeSource(
            () => Task.FromException<ulong>(new InvalidOperationException("boom")));
        var freedesktop = new FreedesktopScreenSaverIdleTimeSource(
            () => Task.FromException<uint>(new InvalidOperationException("boom")));

        Assert.Equal(TimeSpan.Zero, mutter.GetIdleTime());
        Assert.Equal(TimeSpan.Zero, freedesktop.GetIdleTime());
    }

    [Fact]
    public void Mutter_TryCreate_returns_null_when_session_bus_unavailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.Null(MutterIdleMonitorIdleTimeSource.TryCreate(BogusBusAddress, probeTimeoutMs: 1000));
    }

    [Fact]
    public void Freedesktop_TryCreate_returns_null_when_session_bus_unavailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.Null(FreedesktopScreenSaverIdleTimeSource.TryCreate(BogusBusAddress, probeTimeoutMs: 1000));
    }
}
