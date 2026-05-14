using System.Text;

namespace MccTui;

public static class DebugLogger
{
    private static string? _logPath;
    private static readonly object _lock = new();

    public static void Initialize(string logDir)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        _logPath = Path.Combine(logDir, $"MCC-TUI-{ts}.log");
    }

    public static void Log(string message)
    {
        if (!LocalizationManager.IsDebugEnabled || _logPath == null)
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            File.AppendAllText(_logPath, line, Encoding.UTF8);
        }
    }
}
