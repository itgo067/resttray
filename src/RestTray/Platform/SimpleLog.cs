using RestTray.Core.Config;

namespace RestTray.Platform;

/// <summary>
/// 极简日志:同时写控制台和配置目录下的 resttray.log。
/// 托盘应用无控制台时,文件日志是主要排查手段。
/// </summary>
public static class SimpleLog
{
    private static readonly object Sync = new();
    private static string? _filePath;

    private static string FilePath => _filePath ??= Path.Combine(ConfigStore.ConfigDirectory, "resttray.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);
        try
        {
            lock (Sync)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写失败不影响主流程
        }
    }
}
