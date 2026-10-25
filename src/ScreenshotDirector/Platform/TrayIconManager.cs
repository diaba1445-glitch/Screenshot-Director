using System.Drawing;
using System.Windows.Forms;
using ScreenshotDirector.Core.Interfaces;

namespace ScreenshotDirector.Platform;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ICaptureDetectionService _detectionService;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;
    private readonly IAppLogger _logger;

    private readonly ToolStripMenuItem _pauseItem;
    private bool _disposed;

    public NotifyIcon NotifyIcon => _notifyIcon;

    public TrayIconManager(
        ICaptureDetectionService detectionService,
        Action openSettingsAction,
        Action exitAction,
        IAppLogger logger)
    {
        _detectionService = detectionService;
        _openSettingsAction = openSettingsAction;
        _exitAction = exitAction;
        _logger = logger;

        _notifyIcon = new NotifyIcon
        {
            Text = "Screenshot Director",
            Visible = true,
            Icon = CreateAppIcon()
        };

        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Control Center", null, (s, e) => _openSettingsAction());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        _pauseItem = new ToolStripMenuItem("Pause Detection", null, OnTogglePause);
        var manageItem = new ToolStripMenuItem("Manage Presets", null, (s, e) => _openSettingsAction());
        var exitItem = new ToolStripMenuItem("Stop Program & Exit", null, (s, e) => _exitAction());

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(_pauseItem);
        contextMenu.Items.Add(manageItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => _openSettingsAction();
    }

    private void OnTogglePause(object? sender, EventArgs e)
    {
        if (_detectionService.IsPaused)
        {
            _detectionService.Resume();
            _pauseItem.Text = "Pause Detection";
            _notifyIcon.Text = "Screenshot Director (Active)";
        }
        else
        {
            _detectionService.Pause();
            _pauseItem.Text = "Resume Detection";
            _notifyIcon.Text = "Screenshot Director (Paused)";
        }
    }

    private static Icon CreateAppIcon()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var icoPath = System.IO.Path.Combine(baseDir, "Assets", "app.ico");
            if (System.IO.File.Exists(icoPath))
            {
                return new Icon(icoPath, 32, 32);
            }

            var resourceUri = new Uri("pack://application:,,,/ScreenshotDirector;component/Assets/app.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream, 32, 32);
            }
        }
        catch { }

        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch
        {
            // Ignore during process termination
        }
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, int x, int y, int width, int height, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + width - 2 * radius, y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + width - 2 * radius, y + height - 2 * radius, radius * 2, 2 * radius, 0, 90);
        path.AddArc(x, y + height - 2 * radius, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
