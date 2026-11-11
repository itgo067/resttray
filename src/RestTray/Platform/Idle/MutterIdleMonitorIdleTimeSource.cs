using RestTray.Core.Abstractions;
using Tmds.DBus;

namespace RestTray.Platform.Idle;

/// <summary>
/// Wayland/GNOME 系(GNOME Shell、Cinnamon Muffin)空闲检测:
/// 通过 session D-Bus 调用 org.gnome.Mutter.IdleMonitor.GetIdletime(返回空闲毫秒数)。
/// 服务不存在、方法报错或超时都视为不可用,由工厂继续降级。
/// </summary>
internal sealed class MutterIdleMonitorIdleTimeSource : IIdleTimeSource
{
    private const string ServiceName = "org.gnome.Mutter.IdleMonitor";
    private const string ObjectPath = "/org/gnome/Mutter/IdleMonitor/Core";

    private readonly Func<Task<ulong>> _getIdleTimeMs;
    // 保持连接存活:Tmds.DBus 代理由连接驱动,连接被回收后查询会失败
    private readonly Connection? _connection;

    public string Name => "D-Bus GNOME IdleMonitor";

    /// <summary>测试用:直接注入查询委托。</summary>
    internal MutterIdleMonitorIdleTimeSource(Func<Task<ulong>> getIdleTimeMs)
    {
        _getIdleTimeMs = getIdleTimeMs;
    }

    private MutterIdleMonitorIdleTimeSource(Connection connection, IMutterIdleMonitor proxy)
    {
        _connection = connection;
        _getIdleTimeMs = proxy.GetIdletimeAsync;
    }

    /// <summary>
    /// 连接 session bus 并实际调用一次 GetIdletime 做探测;任何失败(无 bus、
    /// 服务未注册、方法不支持、超时)都返回 null。
    /// </summary>
    /// <param name="busAddress">D-Bus 地址;null 表示使用 DBUS_SESSION_BUS_ADDRESS(测试可注入无效地址验证降级)。</param>
    /// <param name="probeTimeoutMs">连接与单次调用的超时毫秒数。</param>
    public static MutterIdleMonitorIdleTimeSource? TryCreate(string? busAddress = null, int probeTimeoutMs = 2000)
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

            var proxy = connection.CreateProxy<IMutterIdleMonitor>(ServiceName, new ObjectPath(ObjectPath));
            if (!proxy.GetIdletimeAsync().Wait(probeTimeoutMs))
            {
                connection.Dispose();
                return null;
            }

            return new MutterIdleMonitorIdleTimeSource(connection, proxy);
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
            var task = _getIdleTimeMs();
            if (!task.Wait(1000))
                return TimeSpan.Zero;
            var ms = task.Result > long.MaxValue ? long.MaxValue : (long)task.Result;
            return TimeSpan.FromMilliseconds(ms);
        }
        catch
        {
            // 运行期查询失败按空闲 0 处理(接口约定:不抛异常)
            return TimeSpan.Zero;
        }
    }
}

[DBusInterface("org.gnome.Mutter.IdleMonitor")]
public interface IMutterIdleMonitor : IDBusObject
{
    Task<ulong> GetIdletimeAsync();
}
