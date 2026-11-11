using RestTray.Core.Abstractions;
using Tmds.DBus;

namespace RestTray.Platform.Idle;

/// <summary>
/// freedesktop 标准空闲检测:org.freedesktop.ScreenSaver.GetSessionIdleTime(返回空闲秒数)。
/// 作为 GNOME IdleMonitor 之后的通用兜底;注意 KDE Plasma(Wayland)虽然注册该服务,
/// 但调用会返回 NotSupported 错误,探测阶段即被淘汰,由工厂继续降级。
/// </summary>
internal sealed class FreedesktopScreenSaverIdleTimeSource : IIdleTimeSource
{
    private const string ServiceName = "org.freedesktop.ScreenSaver";
    private const string ObjectPath = "/ScreenSaver";

    private readonly Func<Task<uint>> _getIdleTimeSeconds;
    // 保持连接存活:Tmds.DBus 代理由连接驱动,连接被回收后查询会失败
    private readonly Connection? _connection;

    public string Name => "D-Bus freedesktop ScreenSaver";

    /// <summary>测试用:直接注入查询委托。</summary>
    internal FreedesktopScreenSaverIdleTimeSource(Func<Task<uint>> getIdleTimeSeconds)
    {
        _getIdleTimeSeconds = getIdleTimeSeconds;
    }

    private FreedesktopScreenSaverIdleTimeSource(Connection connection, IFreedesktopScreenSaver proxy)
    {
        _connection = connection;
        _getIdleTimeSeconds = proxy.GetSessionIdleTimeAsync;
    }

    /// <summary>
    /// 连接 session bus 并实际调用一次 GetSessionIdleTime 做探测;任何失败(无 bus、
    /// 服务未注册、方法不支持、超时)都返回 null。
    /// </summary>
    /// <param name="busAddress">D-Bus 地址;null 表示使用 DBUS_SESSION_BUS_ADDRESS(测试可注入无效地址验证降级)。</param>
    /// <param name="probeTimeoutMs">连接与单次调用的超时毫秒数。</param>
    public static FreedesktopScreenSaverIdleTimeSource? TryCreate(string? busAddress = null, int probeTimeoutMs = 2000)
    {
        Connection? connection = null;
        try
        {
            connection = new Connection(busAddress ?? Address.Session);
            if (!connection.ConnectAsync().Wait(probeTimeoutMs))
            {
                connection.Dispose();
                return null;
            }

            var proxy = connection.CreateProxy<IFreedesktopScreenSaver>(ServiceName, new ObjectPath(ObjectPath));
            if (!proxy.GetSessionIdleTimeAsync().Wait(probeTimeoutMs))
            {
                connection.Dispose();
                return null;
            }

            return new FreedesktopScreenSaverIdleTimeSource(connection, proxy);
        }
        catch
        {
            try { connection?.Dispose(); } catch { /* 探测失败时忽略清理异常 */ }
            return null;
        }
    }

    public TimeSpan GetIdleTime()
    {
        try
        {
            var task = _getIdleTimeSeconds();
            if (!task.Wait(1000))
                return TimeSpan.Zero;
            return TimeSpan.FromSeconds(task.Result);
        }
        catch
        {
            // 运行期查询失败按空闲 0 处理(接口约定:不抛异常)
            return TimeSpan.Zero;
        }
    }
}

[DBusInterface("org.freedesktop.ScreenSaver")]
public interface IFreedesktopScreenSaver : IDBusObject
{
    Task<uint> GetSessionIdleTimeAsync();
}
