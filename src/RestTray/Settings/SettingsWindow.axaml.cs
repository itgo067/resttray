using AtomUI.Desktop.Controls;
using Avalonia.Interactivity;
using RestTray.Core.Config;
using RestTray.Platform;

namespace RestTray.Settings;

/// <summary>
/// 设置窗口(AtomUI 控件)。修改的是与引擎共享的同一个 <see cref="ReminderConfig"/> 实例,
/// 引擎每轮 Tick 都重新读取派生值,因此保存后立即生效,同时落盘 config.json。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ReminderConfig _config = null!;

    /// <summary>仅供 XAML 设计器使用,运行时请使用带配置的构造函数。</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(ReminderConfig config)
    {
        _config = config;
        InitializeComponent();

        RestIntervalInput.Value = config.RestIntervalMinutes;
        IdleThresholdInput.Value = config.IdleThresholdMinutes;
        LongBreakInput.Value = config.LongBreakMinutes;
        SleepTimeInput.Text = config.SleepTime;
        EscalationIntervalInput.Value = config.SleepEscalationIntervalMinutes;

        SaveButton.Click += OnSave;
        CancelButton.Click += (_, _) => Close();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // 睡觉时间格式校验,写坏不保存
        var sleepTime = (SleepTimeInput.Text ?? string.Empty).Trim();
        if (!TimeOnly.TryParse(sleepTime, out _))
        {
            SimpleLog.Warn($"设置保存被拒绝:睡觉时间格式无效 \"{sleepTime}\"");
            SleepTimeInput.Focus();
            return;
        }

        _config.RestIntervalMinutes = (int)(RestIntervalInput.Value ?? 45);
        _config.IdleThresholdMinutes = (int)(IdleThresholdInput.Value ?? 5);
        _config.LongBreakMinutes = (int)(LongBreakInput.Value ?? 10);
        _config.SleepTime = sleepTime;
        _config.SleepEscalationIntervalMinutes = (int)(EscalationIntervalInput.Value ?? 15);

        try
        {
            ConfigStore.Save(_config);
            SimpleLog.Info("设置已保存并立即生效。");
        }
        catch (Exception ex)
        {
            SimpleLog.Error("配置文件写入失败", ex);
        }

        Close();
    }
}
