using System.Windows.Interop;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Platform;

namespace ScreenshotDirector.Services;

public class ClipboardListener : IClipboardListener
{
    private readonly IAppLogger _logger;
    private HwndSource? _hwndSource;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isListening;
    private bool _disposed;

    public event EventHandler? ClipboardUpdated;

    public bool IsListening => _isListening;

    public ClipboardListener(IAppLogger logger)
    {
        _logger = logger;
    }

    public void StartListening(IntPtr hwnd = default)
    {
        if (_isListening) return;

        try
        {
            if (hwnd == IntPtr.Zero)
            {
                var parameters = new HwndSourceParameters("ScreenshotDirectorClipboardListener")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = 0,
                    PositionY = 0,
                    WindowStyle = 0x800000 // WS_BORDER or minimal
                };
                _hwndSource = new HwndSource(parameters);
                _hwndSource.AddHook(WndProc);
                _hwnd = _hwndSource.Handle;
            }
            else
            {
                _hwnd = hwnd;
            }

            bool success = NativeMethods.AddClipboardFormatListener(_hwnd);
            if (success)
            {
                _isListening = true;
                _logger.Info($"Registered AddClipboardFormatListener on HWND {_hwnd}");
            }
            else
            {
                _logger.Error($"Failed AddClipboardFormatListener on HWND {_hwnd}, Win32Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Exception while registering clipboard format listener", ex);
        }
    }

    public void StopListening()
    {
        if (!_isListening) return;

        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
                _logger.Info($"Removed clipboard format listener from HWND {_hwnd}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Exception while unregistering clipboard format listener", ex);
        }
        finally
        {
            _isListening = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            try
            {
                ClipboardUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error("Error handling ClipboardUpdated event in WndProc", ex);
            }
            handled = false;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopListening();

        if (_hwndSource != null)
        {
            try
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource.Dispose();
                _hwndSource = null;
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
