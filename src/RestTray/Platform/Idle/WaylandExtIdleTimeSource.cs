using System.Runtime.InteropServices;
using System.Text;
using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// Wayland 原生空闲检测:ext-idle-notify-v1 协议(KDE KWin、wlroots 系、GNOME Mutter 均支持)。
/// 纯托管 P/Invoke libwayland-client,不引入额外原生依赖(发行版桌面会话必然已装该库)。
///
/// 协议只提供"空闲超过阈值 / 恢复活动"事件,没有精确空闲时长查询。策略:
/// 注册一个 5 秒阈值的 idle notification,收到 idled 即认为用户至少已空闲 5 秒,
/// 空闲起点按 idled 事件时刻回推 5 秒估算(误差 ≤5 秒,对分钟级休息提醒足够);
/// 收到 resumed 立即归零。
///
/// KDE Plasma Wayland 是本实现的主要受益环境:其 org.freedesktop.ScreenSaver
/// 的 GetSessionIdleTime 会返回 NotSupported,D-Bus 路线全部失效,只剩这条原生路径。
/// </summary>
internal sealed class WaylandExtIdleTimeSource : IIdleTimeSource
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(5);

    // 协议事件由 DispatchLoop 后台线程写入,GetIdleTime 在引擎轮询线程读取:全部走 volatile 语义
    private static bool s_idled;
    private static bool s_alive;
    private static DateTimeOffset s_idledAtUtc;

    public string Name => "Wayland ext-idle-notify";

    public TimeSpan GetIdleTime() => ComputeIdle(
        Volatile.Read(ref s_alive), Volatile.Read(ref s_idled), s_idledAtUtc,
        DateTimeOffset.UtcNow, IdleThreshold);

    /// <summary>空闲时长估算(纯函数,便于单测):未空闲/连接已断开 → 0;否则 (now - idledAt) + 阈值。</summary>
    internal static TimeSpan ComputeIdle(bool alive, bool idled, DateTimeOffset idledAtUtc,
        DateTimeOffset nowUtc, TimeSpan threshold)
    {
        if (!alive || !idled)
            return TimeSpan.Zero;
        var idle = (nowUtc - idledAtUtc) + threshold;
        return idle < TimeSpan.Zero ? TimeSpan.Zero : idle;
    }

    /// <summary>
    /// 连接 Wayland 合成器、确认其支持 ext-idle-notify-v1 并注册 idle notification;
    /// 任何一步失败都返回 null(由工厂继续降级),不抛异常。
    /// </summary>
    public static WaylandExtIdleTimeSource? TryCreate()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            if (!NativeProtocol.EnsureReady())
                return null;

            display = Native.wl_display_connect(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return null;

            var registry = MarshalGetRegistry(display);
            if (registry == IntPtr.Zero
                || Native.wl_proxy_add_listener(registry, NativeProtocol.RegistryListener, IntPtr.Zero) != 0
                || Native.wl_display_roundtrip(display) < 0)
            {
                Native.wl_display_disconnect(display);
                return null;
            }

            // registry 回调在 roundtrip 中同步完成,可直接读取收集到的 global
            if (NativeProtocol.NotifierGlobalName == 0 || NativeProtocol.SeatGlobalName == 0)
            {
                Native.wl_display_disconnect(display);
                return null; // 合成器不支持 ext-idle-notify-v1 或未提供 seat
            }

            var notifier = RegistryBind(registry, NativeProtocol.NotifierGlobalName,
                NativeProtocol.IdleNotifierInterface, 1);
            var seat = RegistryBind(registry, NativeProtocol.SeatGlobalName, Native.WlSeatInterface, 1);
            if (notifier == IntPtr.Zero || seat == IntPtr.Zero
                // seat 会立即推送 capabilities 事件,无 listener 会导致 libwayland abort,必须挂空监听
                || Native.wl_proxy_add_listener(seat, NativeProtocol.SeatListener, IntPtr.Zero) != 0)
            {
                Native.wl_display_disconnect(display);
                return null;
            }

            var notification = GetIdleNotification(notifier, seat, (uint)IdleThreshold.TotalMilliseconds);
            if (notification == IntPtr.Zero
                || Native.wl_proxy_add_listener(notification, NativeProtocol.NotificationListener, IntPtr.Zero) != 0
                || Native.wl_display_roundtrip(display) < 0)
            {
                Native.wl_display_disconnect(display);
                return null;
            }

            Volatile.Write(ref s_idled, false);
            Volatile.Write(ref s_alive, true);

            var thread = new Thread(() => DispatchLoop(display))
            {
                IsBackground = true,
                Name = "RestTray.WaylandIdle",
            };
            thread.Start();
            return new WaylandExtIdleTimeSource();
        }
        catch
        {
            // 库缺失、协议不匹配、连接中断等任何异常都按不可用处理
            if (display != IntPtr.Zero)
            {
                try { Native.wl_display_disconnect(display); } catch { /* 忽略 */ }
            }
            return null;
        }
    }

    private static void DispatchLoop(IntPtr display)
    {
        try
        {
            while (Native.wl_display_dispatch(display) >= 0)
            {
            }
        }
        catch
        {
            // 显示连接断开:进入非存活状态
        }
        Volatile.Write(ref s_idled, false);
        Volatile.Write(ref s_alive, false);
    }

    private static void OnIdled()
    {
        s_idledAtUtc = DateTimeOffset.UtcNow;
        Volatile.Write(ref s_idled, true);
    }

    private static void OnResumed() => Volatile.Write(ref s_idled, false);

    // ---------- Wayland 协议封送 ----------

    /// <summary>union wl_argument(8 字节):按字段偏移 0 重叠存放各类型。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct WlArgument
    {
        [FieldOffset(0)] public uint U;
        [FieldOffset(0)] public IntPtr S;
        [FieldOffset(0)] public IntPtr O;
    }

    /// <summary>wl_display.get_registry(opcode 1):单 new_id 参数。</summary>
    private static IntPtr MarshalGetRegistry(IntPtr display)
    {
        var args = new WlArgument[1]; // new_id 占位(NULL)
        return Native.wl_proxy_marshal_array_flags(display, 1, Native.WlRegistryInterface, 1, 0, args);
    }

    /// <summary>wl_registry.bind(opcode 0,签名 "usun"):动态接口绑定。</summary>
    private static IntPtr RegistryBind(IntPtr registry, uint name, IntPtr iface, uint version)
    {
        var args = new WlArgument[4];
        args[0].U = name;
        args[1].S = Marshal.ReadIntPtr(iface, 0); // wl_interface.name(接口名字符串指针)
        args[2].U = version;
        // args[3] 为 new_id 占位(NULL)
        return Native.wl_proxy_marshal_array_flags(registry, 0, iface, version, 0, args);
    }

    /// <summary>ext_idle_notifier_v1.get_idle_notification(opcode 1,签名 "nuoo"),surface 传 NULL。</summary>
    private static IntPtr GetIdleNotification(IntPtr notifier, IntPtr seat, uint timeoutMs)
    {
        var args = new WlArgument[4];
        // args[0] 为 new_id 占位(NULL)
        args[1].U = timeoutMs;
        args[2].O = seat;
        // args[3] surface = NULL(协议允许)
        return Native.wl_proxy_marshal_array_flags(notifier, 1,
            NativeProtocol.IdleNotificationInterface, 1, 0, args);
    }

    /// <summary>
    /// ext-idle-notify-v1 协议对象与监听器。ext_idle_notifier_v1 / ext_idle_notification_v1
    /// 不属于 libwayland 内置接口(无导出符号),需要按 wl_interface/wl_message 布局手工构建,
    /// 分配在进程生命周期的非托管内存中。
    /// </summary>
    private static class NativeProtocol
    {
        internal static IntPtr IdleNotifierInterface { get; private set; }
        internal static IntPtr IdleNotificationInterface { get; private set; }
        internal static IntPtr RegistryListener { get; private set; }
        internal static IntPtr NotificationListener { get; private set; }
        internal static IntPtr SeatListener { get; private set; }

        // registry 枚举结果(TryCreate 线程 roundtrip 内同步写入)
        internal static uint NotifierGlobalName { get; private set; }
        internal static uint SeatGlobalName { get; private set; }

        // 回调委托必须静态持有,防止 GC 后 native 回调悬空
        private static readonly RegistryGlobalDelegate s_onGlobal = OnGlobal;
        private static readonly RegistryGlobalRemoveDelegate s_onGlobalRemove = OnGlobalRemove;
        private static readonly IdleEventDelegate s_onIdled = OnIdledNative;
        private static readonly IdleEventDelegate s_onResumed = OnResumedNative;
        private static readonly SeatCapabilitiesDelegate s_onSeatCapabilities = OnSeatCapabilities;
        private static readonly SeatNameDelegate s_onSeatName = OnSeatName;

        private static bool s_ready;

        internal static bool EnsureReady()
        {
            if (s_ready)
                return true;
            if (!Native.TryLoad())
                return false;

            IdleNotificationInterface = AllocInterface(
                "ext_idle_notification_v1", version: 1,
                methods: new[] { AllocMessage("destroy", "", null) },
                events: new[] { AllocMessage("idled", "", null), AllocMessage("resumed", "", null) });

            IdleNotifierInterface = AllocInterface(
                "ext_idle_notifier_v1", version: 1,
                methods: new[]
                {
                    AllocMessage("destroy", "", null),
                    // get_idle_notification(new_id, uint timeout, wl_seat, wl_surface|null)
                    // 签名中的 ? 表示该 object 参数允许为 NULL(surface 我们不用,传空)
                    AllocMessage("get_idle_notification", "nuo?o",
                        new[] { IdleNotificationInterface, IntPtr.Zero, Native.WlSeatInterface, Native.WlSurfaceInterface }),
                },
                events: Array.Empty<IntPtr>());

            RegistryListener = AllocListener(
                Marshal.GetFunctionPointerForDelegate(s_onGlobal),
                Marshal.GetFunctionPointerForDelegate(s_onGlobalRemove));
            NotificationListener = AllocListener(
                Marshal.GetFunctionPointerForDelegate(s_onIdled),
                Marshal.GetFunctionPointerForDelegate(s_onResumed));
            SeatListener = AllocListener(
                Marshal.GetFunctionPointerForDelegate(s_onSeatCapabilities),
                Marshal.GetFunctionPointerForDelegate(s_onSeatName));

            s_ready = true;
            return true;
        }

        private static void OnGlobal(IntPtr data, IntPtr registry, uint name, IntPtr ifaceNamePtr, uint version)
        {
            var iface = Marshal.PtrToStringUTF8(ifaceNamePtr);
            if (iface == "ext_idle_notifier_v1" && NotifierGlobalName == 0)
                NotifierGlobalName = name;
            else if (iface == "wl_seat" && SeatGlobalName == 0)
                SeatGlobalName = name;
        }

        private static void OnGlobalRemove(IntPtr data, IntPtr registry, uint name) { }

        private static void OnIdledNative(IntPtr data, IntPtr notification) => OnIdled();

        private static void OnResumedNative(IntPtr data, IntPtr notification) => OnResumed();

        private static void OnSeatCapabilities(IntPtr data, IntPtr seat, uint capabilities) { }

        private static void OnSeatName(IntPtr data, IntPtr seat, IntPtr name) { }

        private static IntPtr AllocListener(params IntPtr[] handlers)
        {
            var p = Marshal.AllocHGlobal(IntPtr.Size * handlers.Length);
            for (var i = 0; i < handlers.Length; i++)
                Marshal.WriteIntPtr(p, i * IntPtr.Size, handlers[i]);
            return p;
        }

        /// <summary>构建一条 wl_message { name, signature, types },返回指向它的指针。</summary>
        private static IntPtr AllocMessage(string name, string signature, IntPtr[]? argTypes)
        {
            IntPtr typesPtr = IntPtr.Zero;
            if (argTypes is not null)
            {
                typesPtr = Marshal.AllocHGlobal(IntPtr.Size * (argTypes.Length + 1));
                for (var i = 0; i < argTypes.Length; i++)
                    Marshal.WriteIntPtr(typesPtr, i * IntPtr.Size, argTypes[i]);
                Marshal.WriteIntPtr(typesPtr, argTypes.Length * IntPtr.Size, IntPtr.Zero);
            }

            var msg = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(msg, 0, AllocUtf8(name));
            Marshal.WriteIntPtr(msg, IntPtr.Size, AllocUtf8(signature));
            Marshal.WriteIntPtr(msg, 2 * IntPtr.Size, typesPtr);
            return msg;
        }

        /// <summary>
        /// 构建 wl_interface { name; int version; int method_count; wl_message* methods;
        /// int event_count; wl_message* events },x86-64 下大小 40 字节。
        /// </summary>
        private static IntPtr AllocInterface(string name, int version, IntPtr[] methods, IntPtr[] events)
        {
            var iface = Marshal.AllocHGlobal(40);
            Marshal.WriteIntPtr(iface, 0, AllocUtf8(name));
            Marshal.WriteInt32(iface, 8, version);
            Marshal.WriteInt32(iface, 12, methods.Length);
            Marshal.WriteIntPtr(iface, 16, AllocMessageArray(methods));
            Marshal.WriteInt32(iface, 24, events.Length);
            Marshal.WriteIntPtr(iface, 32, AllocMessageArray(events));
            return iface;
        }

        /// <summary>wl_message 结构体连续数组(每条 24 字节,内容为指针拷贝)。</summary>
        private static IntPtr AllocMessageArray(IntPtr[] messages)
        {
            if (messages.Length == 0)
                return IntPtr.Zero;
            const int messageSize = 3 * 8;
            var arr = Marshal.AllocHGlobal(messageSize * messages.Length);
            for (var i = 0; i < messages.Length; i++)
            {
                var dst = arr + i * messageSize;
                Marshal.WriteIntPtr(dst, 0, Marshal.ReadIntPtr(messages[i], 0));
                Marshal.WriteIntPtr(dst, 8, Marshal.ReadIntPtr(messages[i], 8));
                Marshal.WriteIntPtr(dst, 16, Marshal.ReadIntPtr(messages[i], 16));
            }
            return arr;
        }

        private static IntPtr AllocUtf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + '\0');
            var p = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RegistryGlobalDelegate(IntPtr data, IntPtr registry, uint name, IntPtr ifaceName, uint version);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RegistryGlobalRemoveDelegate(IntPtr data, IntPtr registry, uint name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void IdleEventDelegate(IntPtr data, IntPtr notification);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SeatCapabilitiesDelegate(IntPtr data, IntPtr seat, uint capabilities);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SeatNameDelegate(IntPtr data, IntPtr seat, IntPtr name);
    }

    private static class Native
    {
        private const string Lib = "libwayland-client.so.0";

        internal static IntPtr WlRegistryInterface { get; private set; }
        internal static IntPtr WlSeatInterface { get; private set; }
        internal static IntPtr WlSurfaceInterface { get; private set; }

        internal static bool TryLoad()
        {
            if (WlRegistryInterface != IntPtr.Zero)
                return true;
            try
            {
                var lib = NativeLibrary.Load(Lib);
                WlRegistryInterface = NativeLibrary.GetExport(lib, "wl_registry_interface");
                WlSeatInterface = NativeLibrary.GetExport(lib, "wl_seat_interface");
                WlSurfaceInterface = NativeLibrary.GetExport(lib, "wl_surface_interface");
                return true;
            }
            catch
            {
                return false; // 无 libwayland(极小概率)/符号缺失:按不可用降级
            }
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wl_display_connect(IntPtr name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wl_display_disconnect(IntPtr display);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int wl_display_roundtrip(IntPtr display);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int wl_display_dispatch(IntPtr display);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wl_proxy_marshal_array_flags(
            IntPtr proxy, uint opcode, IntPtr iface, uint version, uint flags, WlArgument[] args);
    }
}
