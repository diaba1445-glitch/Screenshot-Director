using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Platform;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class CapturePopupView : Window
{
    private CapturePopupViewModel? ViewModel => DataContext as CapturePopupViewModel;
    private bool _isClosing = false;

    public CapturePopupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel?.Dismiss();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var pos = ViewModel?.Position ?? PopupPosition.BottomRight;
        PositionOnScreen(pos);

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

        // Focus window so number shortcuts work immediately
        Focus();
    }

    private void OnWindowMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel?.OnPointerEnterWindow();
    }

    private void OnWindowMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel?.OnPointerLeaveWindow();
    }

    public void AnimateClose()
    {
        if (_isClosing) return;
        _isClosing = true;

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

    public void PositionOnScreen(PopupPosition position = PopupPosition.BottomRight)
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

    private async void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel == null) return;

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            if (ViewModel.IsCustomPathSelected)
            {
                ViewModel.IsCustomPathSelected = false;
                UpdateLayout();
                PositionOnScreen(ViewModel.Position);
            }
            else
            {
                ViewModel.Dismiss();
            }
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
            ViewModel.Dismiss();
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
            UpdateLayout();
            PositionOnScreen(ViewModel.Position);
        }
    }

    private void OnClearSessionDefaultClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ClearSessionDefault();
            UpdateLayout();
            PositionOnScreen(ViewModel.Position);
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

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel.SelectedCustomPath = dialog.SelectedPath;
            ViewModel.IsCustomPathSelected = true;
            UpdateLayout();
            PositionOnScreen(ViewModel.Position);
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
            UpdateLayout();
            PositionOnScreen(ViewModel.Position);
        }
    }
}
