using System.IO;
using ScreenshotDirector.Core.Interfaces;

namespace ScreenshotDirector.Infrastructure;

public class AppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private const int MaxLogFiles = 7;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB per file

    public AppLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotDirector",
            "Logs");

        try
        {
            Directory.CreateDirectory(_logDirectory);
            CleanOldLogs();
        }
        catch
        {
            // Logging initialization should never crash the app
        }
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warning(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private void Write(LogLevel level, string message, Exception? ex = null)
    {
        try
        {
            var now = DateTime.Now;
            var fileName = $"screenshotdirector_{now:yyyy-MM-dd}.log";
            var filePath = Path.Combine(_logDirectory, fileName);

            var exDetails = ex != null ? $"{Environment.NewLine}{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}" : string.Empty;
            var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant(),-7}] [Thread-{Environment.CurrentManagedThreadId}] {message}{exDetails}";

            lock (_lock)
            {
                // Check size
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists && fileInfo.Length > MaxFileSizeBytes)
                {
                    // Rotate
                    var rotated = Path.Combine(_logDirectory, $"screenshotdirector_{now:yyyy-MM-dd}_{Guid.NewGuid().ToString("N")[..6]}.log");
                    try
                    {
                        File.Move(filePath, rotated, true);
                    }
                    catch
                    {
                        // Ignore rotation failure
                    }
                }

                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Prevent recursive logging failure or crash
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var dir = new DirectoryInfo(_logDirectory);
            if (!dir.Exists) return;

            var files = dir.GetFiles("screenshotdirector_*.log")
                           .OrderByDescending(f => f.LastWriteTimeUtc)
                           .ToList();

            if (files.Count > MaxLogFiles)
            {
                foreach (var file in files.Skip(MaxLogFiles))
                {
                    try { file.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }
}
