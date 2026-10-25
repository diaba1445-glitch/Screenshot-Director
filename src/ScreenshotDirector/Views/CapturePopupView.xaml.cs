using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenshotDirector.Platform;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class CapturePopupView : Window
{
    private CapturePopupViewModel? ViewModel => DataContext as CapturePopupViewModel;

    public CapturePopupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearCursor();
        if (ViewModel != null)
        {
            ViewModel.RequestClose += (s, args) => Close();
        }

        // Focus window so number shortcuts work immediately
        Focus();
    }

    public void PositionNearCursor()
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                CenterOnPrimaryScreen();
                return;
            }

            var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };

            if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
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

            // Cursor pos in DIPs
            double cursorX = pt.X / dpiX;
            double cursorY = pt.Y / dpiY;

            UpdateLayout();
            double popupWidth = ActualWidth > 0 ? ActualWidth : Width;
            double popupHeight = ActualHeight > 0 ? ActualHeight : 350;

            // Target offset from cursor
            double targetLeft = cursorX + 15;
            double targetTop = cursorY + 15;

            // Clamp / flip if exceeding work area boundaries
            if (targetLeft + popupWidth > workRight)
            {
                targetLeft = cursorX - popupWidth - 15;
            }

            if (targetTop + popupHeight > workBottom)
            {
                targetTop = cursorY - popupHeight - 15;
            }

            // Ensure not off-screen to the left or top
            if (targetLeft < workLeft)
            {
                targetLeft = workLeft + 10;
            }

            if (targetTop < workTop)
            {
                targetTop = workTop + 10;
            }

            Left = targetLeft;
            Top = targetTop;
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

        ViewModel.ResetTimeout();

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            ViewModel.Dismiss();
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

    private async void OnFilenameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel == null) return;

        ViewModel.ResetTimeout();

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            // Save to first destination
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
        ViewModel?.ResetTimeout();
    }

    private void OnWindowMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel?.ResetTimeout();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Dismiss();
    }

    private async void OnDestinationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DestinationItemViewModel item && ViewModel != null)
        {
            await ViewModel.TriggerSaveAsync(item.Folder);
        }
    }
}
