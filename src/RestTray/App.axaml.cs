using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RestTray.Core.Config;
using RestTray.Core.Engine;
using RestTray.Platform;
using RestTray.Platform.Idle;
using RestTray.Platform.Notify;
using RestTray.Tray;

namespace RestTray;

// AtomUI 源生成器要求 Application 为 partial,以便在 Initialize 前完成主题挂载
public partial class App : Application
{
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 无窗口常驻托盘:只允许显式退出(托盘菜单"退出")
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var config = ConfigStore.Load();
            SimpleLog.Info($"配置已加载:{ConfigStore.ConfigPath}");

            var idleSource = IdleTimeSourceFactory.Create();
            var notifier = NotifierFactory.Create();

            var engine = new ReminderEngine(new Core.Abstractions.SystemClock(), idleSource, config);
            engine.RestReminderDue += () =>
                notifier.Show("该休息了",
                    $"你已经连续使用电脑 {config.RestIntervalMinutes} 分钟了,站起来活动一下,看看远处吧。");
            engine.SleepReminderDue += level =>
            {
                var (title, message) = level switch
                {
                    0 => ("该睡觉了", $"已经 {config.SleepTimeOfDay:HH:mm} 了,放下手头的事,去睡觉吧。"),
                    1 => ("真的要睡了", "15 分钟过去了,别再熬了。"),
                    2 => ("熬夜警告", "已经连续提醒两次了,身体要紧!"),
                    _ => ("最后警告", "这是最后一次提醒。再不睡,明天会后悔的。"),
                };
                notifier.Show(title, message);
            };

            _tray = new TrayController(engine, config, desktop);
            _tray.Attach(this);
            SimpleLog.Info("RestTray 已启动。");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
