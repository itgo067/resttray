using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using RestTray.Core.Config;
using RestTray.Core.Engine;
using RestTray.Platform;
using RestTray.Settings;

namespace RestTray.Tray;

/// <summary>
/// 托盘控制器:创建托盘图标与右键菜单,周期驱动状态机并刷新 tooltip。
/// 注意:托盘右键菜单由操作系统原生渲染(NativeMenu),AtomUI 主题不参与其样式。
/// </summary>
public sealed class TrayController : IDisposable
{
    private static readonly TimeSpan PauseDuration = TimeSpan.FromHours(1);

    private readonly ReminderEngine _engine;
    private readonly ReminderConfig _config;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    private TrayIcon? _trayIcon;
    private DispatcherTimer? _timer;
    private NativeMenuItem? _pauseItem;
    private SettingsWindow? _settingsWindow;

    public TrayController(ReminderEngine engine, ReminderConfig config,
        IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _engine = engine;
        _config = config;
        _lifetime = lifetime;
    }

    public void Attach(Application app)
    {
        var menu = new NativeMenu();

        _pauseItem = new NativeMenuItem("暂停提醒 1 小时");
        _pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(_pauseItem);

        var settingsItem = new NativeMenuItem("设置…");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => _lifetime.Shutdown();
        menu.Items.Add(exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://RestTray/Assets/tray-icon.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "RestTray",
            Menu = menu,
        };
        TrayIcon.SetIcons(app, new TrayIcons { _trayIcon });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _config.TickIntervalSeconds)) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        UpdateTooltip();
    }

    private void OnTick()
    {
        _engine.Tick();
        UpdateTooltip();
    }

    private void TogglePause()
    {
        if (_engine.IsPaused)
        {
            _engine.Resume();
            SimpleLog.Info("提醒已恢复。");
        }
        else
        {
            _engine.Pause(PauseDuration);
            SimpleLog.Info($"提醒已暂停 {PauseDuration.TotalMinutes:0} 分钟。");
        }
        UpdateTooltip();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_config);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null)
            return;

        if (_pauseItem is not null)
            _pauseItem.Header = _engine.IsPaused ? "恢复提醒" : "暂停提醒 1 小时";

        if (_engine.IsPaused && _engine.PausedUntil is { } until)
        {
            var remain = until - DateTime.Now;
            _trayIcon.ToolTipText = $"RestTray(已暂停,剩余 {Math.Max(0, (int)remain.TotalMinutes)} 分钟)";
            return;
        }

        var active = (int)_engine.ActiveAccumulated.TotalMinutes;
        var toRest = (int)Math.Ceiling(_engine.TimeToNextRest.TotalMinutes);
        _trayIcon.ToolTipText =
            $"RestTray\n活跃 {active} 分钟 · 距休息提醒 {toRest} 分钟\n睡觉提醒 {_config.SleepTimeOfDay:HH:mm}";
    }

    public void Dispose()
    {
        _timer?.Stop();
    }
}
