using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Platform;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class CapturePopupView : Window
{
    private const int EscapeHotkeyId = 0x5345; // Arbitrary per-window hotkey id

    private CapturePopupViewModel? ViewModel => DataContext as CapturePopupViewModel;
    private bool _isClosing = false;
    private bool _isClosed = false;
    private HwndSource? _escapeHotkeySource;
    private IntPtr _escapeHotkeyWindow = IntPtr.Zero;
    private bool _escapeHotkeyRegistered;
    private SnippingToolWindow? _snippingWindow;
    private bool _hasCursorAnchor;
    private double _cursorAnchorLeft;
    private double _cursorAnchorTop;

    public CapturePopupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UnregisterEscapeHotkey();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _isClosed = true;
        UnregisterEscapeHotkey();

        try { _snippingWindow?.Close(); } catch { }
        _snippingWindow = null;

        ViewModel?.Dismiss();
    }

    /// <summary>
    /// Registers Escape system-wide while this popup is visible, so it can be
    /// dismissed even when the user has clicked into another application.
    /// </summary>
    private void RegisterEscapeHotkey()
    {
        if (_escapeHotkeyRegistered || _isClosing || _isClosed || !IsLoaded)
        {
            return;
        }

        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
            {
                return;
            }

            _escapeHotkeySource = HwndSource.FromHwnd(helper.Handle);
            if (_escapeHotkeySource == null)
            {
                ViewModel?.Logger?.Warning("Could not attach to the popup window for the global Escape hotkey.");
                return;
            }

            if (NativeMethods.RegisterHotKey(
                    helper.Handle,
                    EscapeHotkeyId,
                    NativeMethods.MOD_NOREPEAT,
                    NativeMethods.VK_ESCAPE))
            {
                _escapeHotkeyWindow = helper.Handle;
                _escapeHotkeyRegistered = true;

                try
                {
                    _escapeHotkeySource.AddHook(EscapeHotkeyWndProc);
                    ViewModel?.Logger?.Info("Registered global Escape hotkey for capture popup.");
                }
                catch
                {
                    UnregisterEscapeHotkey();
                    throw;
                }
            }
            else
            {
                _escapeHotkeySource = null;
                int error = Marshal.GetLastWin32Error();
                ViewModel?.Logger?.Warning(
                    $"Could not register global Escape hotkey (Win32 error {error}). Esc stays focus-only.");
            }
        }
        catch (Exception ex)
        {
            ViewModel?.Logger?.Error("Failed to register global Escape hotkey", ex);
        }
    }

    private void UnregisterEscapeHotkey()
    {
        if (_escapeHotkeyRegistered && _escapeHotkeyWindow != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterHotKey(_escapeHotkeyWindow, EscapeHotkeyId);
            }
            catch (Exception ex)
            {
                ViewModel?.Logger?.Warning("Failed to unregister global Escape hotkey", ex);
            }
        }

        _escapeHotkeyRegistered = false;
        _escapeHotkeyWindow = IntPtr.Zero;

        if (_escapeHotkeySource != null)
        {
            try { _escapeHotkeySource.RemoveHook(EscapeHotkeyWndProc); } catch { }
            _escapeHotkeySource = null;
        }
    }

    private void SuspendEscapeHotkey() => UnregisterEscapeHotkey();

    private void ResumeEscapeHotkey()
    {
        // While our own snipping editor or a modal dialog is open, let that
        // window handle Escape normally instead of dismissing the popup.
        if (!_isClosing && !_isClosed && ViewModel?.IsSnippingToolOpen != true)
        {
            RegisterEscapeHotkey();
        }
    }

    private IntPtr EscapeHotkeyWndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == EscapeHotkeyId)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(HandleEscapeKey));
        }

        return IntPtr.Zero;
    }

    private void HandleEscapeKey()
    {
        if (ViewModel == null || _isClosing || _isClosed)
        {
            return;
        }

        if (ViewModel.IsCustomPathSelected)
        {
            ViewModel.IsCustomPathSelected = false;
            RefreshPosition();
        }
        else
        {
            ViewModel.Dismiss();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        bool wasExpanded = ViewModel?.IsDetailsExpanded ?? false;
        ViewModel?.ResetPointerHoverState();

        if (wasExpanded && ViewModel is { IsDetailsExpanded: false })
        {
            RefreshPosition();
        }

        // If our built-in snipping tool is currently open, keep popup open and paused
        if (ViewModel != null && ViewModel.IsSnippingToolOpen)
        {
            return;
        }

        // If the user activated an external image markup editor, dismiss this popup immediately
        // so it does not persist over the editing canvas or cause DWM lag.
        if (NativeMethods.IsForegroundMarkupEditor())
        {
            AnimateClose();
            return;
        }

        // The editor can take over a second to appear after a notification click, so poll briefly.
        _ = WatchForEditorAsync();
    }

    private async Task WatchForEditorAsync()
    {
        try
        {
            for (int i = 0; i < 15 && !_isClosing; i++)
            {
                await Task.Delay(100);
                if (ViewModel != null && ViewModel.IsSnippingToolOpen)
                {
                    return;
                }
                if (!_isClosing && NativeMethods.IsForegroundMarkupEditor())
                {
                    AnimateClose();
                    return;
                }
            }
        }
        catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var pos = ViewModel?.Position ?? PopupPosition.TopRight;
        PositionOnScreen(pos);
        _hasCursorAnchor = pos == PopupPosition.CursorAdjacent;
        _cursorAnchorLeft = Left;
        _cursorAnchorTop = Top;

        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            WindowBlurHelper.EnableBlur(helper.Handle, ViewModel?.IsLightGlass ?? true);
        }
        catch { }

        if (ViewModel != null)
        {
            ViewModel.RequestClose += (s, args) => AnimateClose();
        }

        RegisterEscapeHotkey();

        // Focus window so number shortcuts work immediately
        Focus();
    }

    private void OnWindowMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        bool wasExpanded = ViewModel.IsDetailsExpanded;
        ViewModel.OnPointerEnterWindow();
        if (!wasExpanded && ViewModel.IsDetailsExpanded)
        {
            RefreshPosition();
        }
    }

    private void OnWindowMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        bool wasExpanded = ViewModel.IsDetailsExpanded;
        ViewModel.OnPointerLeaveWindow();
        if (wasExpanded && !ViewModel.IsDetailsExpanded)
        {
            RefreshPosition();
        }
    }

    public void AnimateClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        UnregisterEscapeHotkey();

        var animDuration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fadeAnim = new DoubleAnimation(1.0, 0.0, animDuration) { EasingFunction = ease };
        var slideAnim = new DoubleAnimation(0.0, 24.0, animDuration) { EasingFunction = ease };
        var scaleAnimX = new DoubleAnimation(1.0, 0.95, animDuration) { EasingFunction = ease };
        var scaleAnimY = new DoubleAnimation(1.0, 0.95, animDuration) { EasingFunction = ease };

        fadeAnim.Completed += (s, e) =>
        {
            try { Close(); } catch { }
        };

        BeginAnimation(OpacityProperty, fadeAnim);
        WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
    }

    public void PositionNearCursor()
    {
        PositionOnScreen(PopupPosition.CursorAdjacent);
    }

    public void PositionOnScreen(PopupPosition position = PopupPosition.TopRight)
    {
        try
        {
            NativeMethods.POINT pt;
            bool hasCursor = NativeMethods.GetCursorPos(out pt);
            IntPtr monitor = hasCursor
                ? NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;

            var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };

            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref mi))
            {
                CenterOnPrimaryScreen();
                return;
            }

            // Get DPI factor
            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiX = dpi.DpiScaleX;
            double dpiY = dpi.DpiScaleY;

            // Monitor work area in WPF DIPs
            double workLeft = mi.rcWork.Left / dpiX;
            double workTop = mi.rcWork.Top / dpiY;
            double workWidth = mi.rcWork.Width / dpiX;
            double workHeight = mi.rcWork.Height / dpiY;
            double workRight = workLeft + workWidth;
            double workBottom = workTop + workHeight;

            Measure(new System.Windows.Size(Width, double.PositiveInfinity));
            UpdateLayout();
            double popupWidth = ActualWidth > 0 ? ActualWidth : (DesiredSize.Width > 0 ? DesiredSize.Width : Width);
            double popupHeight = ActualHeight > 0 ? ActualHeight : (DesiredSize.Height > 0 ? DesiredSize.Height : 380);

            const double Margin = 24.0;

            switch (position)
            {
                case PopupPosition.BottomRight:
                    Left = workRight - popupWidth - Margin;
                    Top = workBottom - popupHeight - Margin;
                    break;

                case PopupPosition.TopRight:
                    Left = workRight - popupWidth - Margin;
                    Top = workTop + Margin;
                    break;

                case PopupPosition.BottomCenter:
                    Left = workLeft + (workWidth - popupWidth) / 2.0;
                    Top = workBottom - popupHeight - Margin;
                    break;

                case PopupPosition.CursorAdjacent:
                    if (hasCursor)
                    {
                        double cursorX = pt.X / dpiX;
                        double cursorY = pt.Y / dpiY;
                        double targetLeft = cursorX + 15;
                        double targetTop = cursorY + 15;

                        if (targetLeft + popupWidth > workRight)
                            targetLeft = cursorX - popupWidth - 15;
                        if (targetTop + popupHeight > workBottom)
                            targetTop = cursorY - popupHeight - 15;

                        if (targetLeft < workLeft) targetLeft = workLeft + 10;
                        if (targetTop < workTop) targetTop = workTop + 10;

                        Left = targetLeft;
                        Top = targetTop;
                    }
                    else
                    {
                        Left = workRight - popupWidth - Margin;
                        Top = workBottom - popupHeight - Margin;
                    }
                    break;

                default:
                    Left = workRight - popupWidth - Margin;
                    Top = workBottom - popupHeight - Margin;
                    break;
            }
        }
        catch
        {
            CenterOnPrimaryScreen();
        }
    }

    private void CenterOnPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - 350) / 2;
    }

    /// <summary>
    /// Repositions the popup after it grew or shrank, so an expanded popup
    /// always fits on screen and a collapsed one returns to its anchor.
    /// </summary>
    private void RefreshPosition()
    {
        UpdateLayout();

        var position = ViewModel?.Position ?? PopupPosition.TopRight;
        if (position != PopupPosition.CursorAdjacent || !_hasCursorAnchor)
        {
            PositionOnScreen(position);
            return;
        }

        // A cursor-adjacent popup must not chase the pointer when it resizes;
        // restore the anchor captured at open time and keep it on-screen.
        Left = _cursorAnchorLeft;
        Top = _cursorAnchorTop;
        ClampToWorkArea();
    }

    private void ClampToWorkArea()
    {
        try
        {
            NativeMethods.POINT pt;
            IntPtr monitor = NativeMethods.GetCursorPos(out pt)
                ? NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;

            var mi = new NativeMethods.MONITORINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };

            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref mi))
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            double workLeft = mi.rcWork.Left / dpi.DpiScaleX;
            double workTop = mi.rcWork.Top / dpi.DpiScaleY;
            double workRight = workLeft + mi.rcWork.Width / dpi.DpiScaleX;
            double workBottom = workTop + mi.rcWork.Height / dpi.DpiScaleY;

            double popupWidth = ActualWidth > 0 ? ActualWidth : Width;
            double popupHeight = ActualHeight > 0 ? ActualHeight : 380;

            const double Edge = 10.0;

            if (Left < workLeft + Edge) Left = workLeft + Edge;
            if (Top < workTop + Edge) Top = workTop + Edge;
            if (Left + popupWidth > workRight - Edge) Left = Math.Max(workLeft + Edge, workRight - popupWidth - Edge);
            if (Top + popupHeight > workBottom - Edge) Top = Math.Max(workTop + Edge, workBottom - popupHeight - Edge);
        }
        catch
        {
            // Positioning is best-effort and must never block the popup.
        }
    }

    private async void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel == null) return;

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            HandleEscapeKey();
            return;
        }

        // If focus is NOT inside the filename text box, allow 1-9 shortcuts
        if (!FilenameTextBox.IsFocused)
        {
            int number = -1;
            if (e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
            {
                number = e.Key - System.Windows.Input.Key.D0;
            }
            else if (e.Key >= System.Windows.Input.Key.NumPad1 && e.Key <= System.Windows.Input.Key.NumPad9)
            {
                number = e.Key - System.Windows.Input.Key.NumPad0;
            }

            if (number >= 1 && number <= 9)
            {
                e.Handled = true;
                await ViewModel.HandleShortcutKeyAsync(number);
            }
        }
    }

    private void OnFilenameGotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetEditingFilename(true);
    }

    private void OnFilenameLostFocus(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetEditingFilename(false);
    }

    private async void OnFilenameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel == null) return;

        ViewModel.SetEditingFilename(true);

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            var first = ViewModel.Destinations.FirstOrDefault();
            if (first != null)
            {
                await ViewModel.TriggerSaveAsync(first.Folder);
            }
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            HandleEscapeKey();
        }
    }

    private void OnFilenameTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel?.SetEditingFilename(true);
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.Dismiss();
        }
        else
        {
            AnimateClose();
        }
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleTimerPause();
    }

    private async void OnDestinationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DestinationItemViewModel item && ViewModel != null)
        {
            await ViewModel.TriggerSaveAsync(item.Folder);
        }
    }

    private async void OnSaveSessionDefaultNowClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && !string.IsNullOrWhiteSpace(ViewModel.SessionDefaultPath))
        {
            await ViewModel.TriggerSaveAsync(new DestinationFolder
            {
                Name = ViewModel.SessionDefaultFolderName,
                Path = ViewModel.SessionDefaultPath
            });
        }
    }

    private void OnExpandToFullClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ExpandToFullList();
            RefreshPosition();
        }
    }

    private void OnClearSessionDefaultClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ClearSessionDefault();
            RefreshPosition();
        }
    }

    private void OnSelectPathClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.PauseTimer();

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Destination Folder for this Screenshot",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        SuspendEscapeHotkey();
        try
        {
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ViewModel.SelectedCustomPath = dialog.SelectedPath;
                ViewModel.IsCustomPathSelected = true;
                RefreshPosition();
            }
        }
        finally
        {
            ResumeEscapeHotkey();
        }
    }

    private async void OnConfirmSaveCustomPathClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedCustomPath))
        {
            await ViewModel.SaveToCustomPathAsync(
                ViewModel.SelectedCustomPath, 
                ViewModel.AddCustomPathToPresets, 
                ViewModel.SetAsSessionDefaultOnSave);
        }
    }

    private void OnCancelCustomPathClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsCustomPathSelected = false;
            RefreshPosition();
        }
    }

    private void OnThumbnailButtonClick(object sender, RoutedEventArgs e)
    {
        OpenSnippingTool();
    }

    private void OpenSnippingTool()
    {
        if (ViewModel == null)
        {
            ViewModel?.Logger?.Warning("OpenSnippingTool: ViewModel is null");
            return;
        }

        if (ViewModel.PreviewImage == null)
        {
            ViewModel.Logger?.Warning("OpenSnippingTool: PreviewImage is null");
            return;
        }

        if (ViewModel.IsSnippingToolOpen)
        {
            ViewModel.Logger?.Warning("OpenSnippingTool: SnippingTool is already open");
            return;
        }

        ViewModel.PauseTimer();
        ViewModel.IsSnippingToolOpen = true;

        var snippingVm = new SnippingToolViewModel(
            ViewModel.PreviewImage,
            ViewModel.SaveService,
            ViewModel.NotificationService,
            ViewModel.SettingsService,
            ViewModel.DetectionService,
            ViewModel.Logger);

        bool applied = false;
        snippingVm.ApplyEditsRequested += (newBitmap) =>
        {
            applied = true;
            ViewModel.UpdatePreviewImage(newBitmap);
        };

        snippingVm.SavedDirectly += () =>
        {
            AnimateClose();
        };

        var snippingWindow = new SnippingToolWindow(snippingVm);
        snippingWindow.Owner = this;
        _snippingWindow = snippingWindow;

        snippingWindow.Closed += (s, args) =>
        {
            if (ReferenceEquals(_snippingWindow, snippingWindow))
            {
                _snippingWindow = null;
            }

            if (_isClosed)
            {
                return;
            }

            ViewModel.IsSnippingToolOpen = false;
            if (applied)
            {
                Activate();
                Focus();
            }
            else
            {
                ViewModel.ResumeTimer();
            }

            // The editor covered the popup, so no MouseEnter was raised while
            // it was open. Re-expand the tiles if the pointer is already here.
            if (IsMouseOver)
            {
                ViewModel.OnPointerEnterWindow();
                RefreshPosition();
            }

            ResumeEscapeHotkey();
        };

        // The editor owns Escape while it is open (cancel crop/text, then close).
        SuspendEscapeHotkey();
        snippingWindow.Show();
        snippingWindow.Activate();
    }
}
