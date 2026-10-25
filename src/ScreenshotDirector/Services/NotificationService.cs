using System.Windows.Forms;
using ScreenshotDirector.Core.Interfaces;

namespace ScreenshotDirector.Services;

public class NotificationService : INotificationService
{
    private NotifyIcon? _notifyIcon;
    private readonly IAppLogger _logger;

    public NotificationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void RegisterNotifyIcon(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    public void ShowSuccess(string title, string message, string? targetPath = null)
    {
        _logger.Info($"[Notification] Success: {title} - {message}");
        try
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to show balloon tip notification", ex);
        }
    }

    public void ShowWarning(string title, string message)
    {
        _logger.Warning($"[Notification] Warning: {title} - {message}");
        try
        {
            _notifyIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to show warning balloon tip", ex);
        }
    }

    public void ShowError(string title, string message)
    {
        _logger.Error($"[Notification] Error: {title} - {message}");
        try
        {
            _notifyIcon?.ShowBalloonTip(5000, title, message, ToolTipIcon.Error);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to show error balloon tip", ex);
        }
    }
}
