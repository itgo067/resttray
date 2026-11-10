using RestTray.Core.Abstractions;

namespace RestTray.Platform.Idle;

/// <summary>
/// 按平台选择空闲检测实现:
/// Windows → GetLastInputInfo;Linux → XScreenSaver(libXss)→ xprintidle → 降级为"始终活跃"并记录警告。
/// </summary>
public static class IdleTimeSourceFactory
{
    public static IIdleTimeSource Create()
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
            var xss = XScreenSaverIdleTimeSource.TryCreate();
            if (xss is not null)
            {
                SimpleLog.Info("空闲检测:X11 XScreenSaver 扩展");
                return xss;
            }

            if (XprintidleIdleTimeSource.IsAvailable())
            {
                SimpleLog.Info("空闲检测:xprintidle 外部命令");
                return new XprintidleIdleTimeSource();
            }

            SimpleLog.Warn("未检测到可用的 X11 空闲检测(libXss/xprintidle 均不可用),"
                           + "可能处于纯 Wayland 环境。空闲检测降级为\"始终活跃\":"
                           + "提醒将按人在电脑前处理,人不在时也会照常提醒。");
            return new AlwaysActiveIdleTimeSource();
        }

        SimpleLog.Warn($"未支持的平台({Environment.OSVersion}),空闲检测降级为\"始终活跃\"。");
        return new AlwaysActiveIdleTimeSource();
    }
}
