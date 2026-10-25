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
            // Create a clean programmatic 32x32 camera/folder icon
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(0, 122, 204));
                g.FillRoundedRectangle(brush, 2, 6, 28, 20, 4);

                using var lensBrush = new SolidBrush(Color.White);
                g.FillEllipse(lensBrush, 10, 10, 12, 12);

                using var innerLensBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
                g.FillEllipse(innerLensBrush, 13, 13, 6, 6);
            }
            var hIcon = bmp.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(hIcon).Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
        catch
        {
            return SystemIcons.Application;
        }
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
