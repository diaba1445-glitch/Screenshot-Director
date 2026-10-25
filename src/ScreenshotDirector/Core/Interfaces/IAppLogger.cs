namespace ScreenshotDirector.Core.Interfaces;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message, Exception? ex = null);
    void Error(string message, Exception? ex = null);
}
