using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Core.Interfaces;

public interface IClipboardListener : IDisposable
{
    event EventHandler? ClipboardUpdated;
    bool IsListening { get; }
    void StartListening(IntPtr hwnd = default);
    void StopListening();
}

public interface ICaptureDetectionService
{
    event EventHandler<CaptureCandidate>? ScreenshotDetected;
    void NotifyClipboardUpdate();
    void SuppressNextUpdate(TimeSpan window);
    void Pause();
    void Resume();
    bool IsPaused { get; }
}

public interface IStartupManager
{
    bool IsStartupEnabled();
    bool SetStartupEnabled(bool enable);
}

public interface ISingleInstanceManager : IDisposable
{
    bool TryAcquireSingleInstance(out bool isPrimaryInstance);
    void SignalPrimaryInstance();
    event EventHandler? SecondInstanceLaunched;
}

public interface INotificationService
{
    void ShowSuccess(string title, string message, string? targetPath = null);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
}
