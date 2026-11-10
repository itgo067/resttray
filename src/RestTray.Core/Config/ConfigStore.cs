using System.Text.Json;

namespace RestTray.Core.Config;

/// <summary>
/// 配置存取:Windows 下为 %APPDATA%/RestTray/config.json,
/// Linux 下为 ~/.config/RestTray/config.json(遵循 XDG)。
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>配置目录(不存在时自动创建)。</summary>
    public static string ConfigDirectory
    {
        get
        {
            // ApplicationData:Windows → %APPDATA%(roaming);Linux → $XDG_CONFIG_HOME 或 ~/.config
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RestTray");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    /// <summary>
    /// 加载配置;文件不存在时写入默认值并返回;文件损坏时回退默认值(不覆盖原文件,便于排查)。
    /// </summary>
    public static ReminderConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            var defaults = new ReminderConfig();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ReminderConfig>(json, JsonOptions) ?? new ReminderConfig();
        }
        catch (Exception)
        {
            return new ReminderConfig();
        }
    }

    public static void Save(ReminderConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
