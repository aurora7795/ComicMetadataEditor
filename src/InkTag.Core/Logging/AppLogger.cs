using System;
using System.IO;

namespace InkTag.Core.Logging;

/// <summary>
/// Thread-safe cross-platform logger writing to standard OS local application data path and console/debug outputs.
/// </summary>
public static class AppLogger
{
    private static readonly object LogLock = new();
    private static string? _logFilePath;

    /// <summary>
    /// Gets the current log file path.
    /// </summary>
    public static string LogFilePath => _logFilePath ??= GetDefaultLogFilePath();

    /// <summary>
    /// Initializes the logger. Can be called with a custom path (e.g. for unit tests).
    /// </summary>
    public static void Initialize(string? customLogPath = null)
    {
        lock (LogLock)
        {
            _logFilePath = customLogPath ?? GetDefaultLogFilePath();

            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        LogInfo("=========================================");
        LogInfo($"AppLogger initialized. OS: {Environment.OSVersion}, Runtime: {Environment.Version}");
        LogInfo($"Log File Path: {LogFilePath}");
    }

    /// <summary>
    /// Resolves the default cross-platform log file path.
    /// Linux: ~/.local/share/InkTag/logs/InkTag.log
    /// Windows: %LocalAppData%\InkTag\logs\InkTag.log
    /// macOS: ~/Library/Application Support/InkTag/logs/InkTag.log
    /// </summary>
    public static string GetDefaultLogFilePath()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
        return Path.Combine(baseDir, "InkTag", "logs", "InkTag.log");
    }

    /// <summary>
    /// Gets or sets whether verbose / debug logging is active.
    /// </summary>
    public static bool IsDebugEnabled { get; set; } = false;

    public static void LogInfo(string message) => Log("INFO", message);
    public static void LogWarning(string message) => Log("WARN", message);
    public static void LogDebug(string message)
    {
        if (IsDebugEnabled)
        {
            Log("DEBUG", message);
        }
    }
    
    public static void LogError(string message, Exception? ex = null)
    {
        string fullMessage = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        Log("ERROR", fullMessage);
    }

    public const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static void RotateLogIfNeeded(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length >= MaxLogFileSizeBytes)
                {
                    string backupPath = path + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(path, backupPath);
                }
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formattedLine = $"[{timestamp}] [{level}] {message}";

        Console.WriteLine(formattedLine);
        System.Diagnostics.Debug.WriteLine(formattedLine);

        try
        {
            lock (LogLock)
            {
                string path = LogFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                RotateLogIfNeeded(path);

                File.AppendAllText(path, formattedLine + Environment.NewLine);
            }
        }
        catch
        {
            // Fallback gracefully if logging fails (e.g. read-only filesystem)
        }
    }

    /// <summary>
    /// Opens the log folder in the default OS file manager cross-platform (Linux: xdg-open, Windows: explorer, macOS: open).
    /// </summary>
    public static void OpenLogFolder()
    {
        string logFile = LogFilePath;
        string dir = Path.GetDirectoryName(logFile) ?? logFile;

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = File.Exists(logFile) ? $"/select,\"{logFile}\"" : $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = File.Exists(logFile) ? $"-R \"{logFile}\"" : $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            else // Linux & Unix
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to open log folder in system file manager.", ex);
        }
    }
}
