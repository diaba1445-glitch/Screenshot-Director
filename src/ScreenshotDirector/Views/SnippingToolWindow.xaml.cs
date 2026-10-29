using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class SnippingToolWindow : Window
{
    public SnippingToolViewModel? ViewModel => DataContext as SnippingToolViewModel;

    public SnippingToolWindow()
    {
        InitializeComponent();
    }

    public SnippingToolWindow(SnippingToolViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += () => Dispatcher.Invoke(Close);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            // Ensure the image source is explicitly assigned (bypasses any binding timing issues)
            ScreenshotImage.Source = ViewModel.CurrentBaseImage;
            ApplyCanvasPixelSize();

            // Track ViewModel changes so crop/undo keep the Image element in sync
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            FitToViewport();

            System.Diagnostics.Debug.WriteLine($"SnippingToolWindow loaded - Image: {ViewModel.ImageWidth}x{ViewModel.ImageHeight}, Source set: {ScreenshotImage.Source != null}");
        }

        Focus();
    }

    private bool _userZoomed;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SnippingToolViewModel.CurrentBaseImage) && ViewModel != null)
        {
            ScreenshotImage.Source = ViewModel.CurrentBaseImage;
            ApplyCanvasPixelSize();
        }
    }

    private void ApplyCanvasPixelSize()
    {
        if (ViewModel == null) return;
        // Fixed pixel-size host lets the Viewbox scale image + annotation overlay together
        CanvasHostGrid.Width = ViewModel.ImageWidth;
        CanvasHostGrid.Height = ViewModel.ImageHeight;
    }

    private void FitToViewport()
    {
        if (ViewModel == null) return;

        // FitToWindow applies its own inner margin, so pass the raw viewport
        double availW = CanvasScrollViewer.ViewportWidth > 8 ? CanvasScrollViewer.ViewportWidth : ActualWidth - 60;
        double availH = CanvasScrollViewer.ViewportHeight > 8 ? CanvasScrollViewer.ViewportHeight : ActualHeight - 140;
        ViewModel.FitToWindow(availW, availH);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Flexible: re-fit the image whenever the window is resized, unless the user chose a zoom.
        // Defer to after the layout pass so ScrollViewer.ViewportWidth/Height reflect the new size.
        if (_userZoomed) return;

        Dispatcher.BeginInvoke(new Action(FitToViewport), DispatcherPriority.Loaded);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null) return;

        // Let the inline text-annotation editor own the keyboard while it has
        // focus: digits, Enter, Escape and Ctrl+Z/Y must edit the text instead
        // of switching tools, closing the window or undoing annotations.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (isCtrl && e.Key == Key.Z)
        {
            e.Handled = true;
            ViewModel.Undo();
            return;
        }

        if (isCtrl && e.Key == Key.Y)
        {
            e.Handled = true;
            ViewModel.Redo();
            return;
        }

        if (isCtrl && e.Key == Key.C)
        {
            e.Handled = true;
            _ = ViewModel.CopyToClipboardAsync();
            return;
        }

        if (isCtrl && e.Key == Key.S)
        {
            e.Handled = true;
            _ = ViewModel.SaveAsAsync();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (FloatingCropBar.Visibility == Visibility.Visible && OverlayCanvas.CurrentCropRect.Width > 5)
            {
                OnConfirmCropClick(sender, e);
            }
            else
            {
                ViewModel.ApplyToPopup();
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (FloatingCropBar.Visibility == Visibility.Visible)
            {
                OnCancelCropClick(sender, e);
            }
            else
            {
                Close();
            }
            return;
        }

        // Quick numeric tool selection (1: Crop, 2: Pen, 3: Highlighter, 4: Arrow, 5: Rect, 6: Ellipse, 7: Text, 8: Redact)
        if (!isCtrl && e.Key >= Key.D1 && e.Key <= Key.D8)
        {
            int num = e.Key - Key.D0;
            switch (num)
            {
                case 1: ViewModel.ActiveMode = SnippingToolMode.Select; break;
                case 2: ViewModel.ActiveMode = SnippingToolMode.Pen; break;
                case 3: ViewModel.ActiveMode = SnippingToolMode.Highlighter; break;
                case 4: ViewModel.ActiveMode = SnippingToolMode.Arrow; break;
                case 5: ViewModel.ActiveMode = SnippingToolMode.Rectangle; break;
                case 6: ViewModel.ActiveMode = SnippingToolMode.Ellipse; break;
                case 7: ViewModel.ActiveMode = SnippingToolMode.Text; break;
                case 8: ViewModel.ActiveMode = SnippingToolMode.Pixelate; break;
            }
        }
    }

    private void OnSelectModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Select;
    }

    private void OnPenModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Pen;
    }

    private void OnHighlighterModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Highlighter;
    }

    private void OnArrowModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Arrow;
    }

    private void OnRectangleModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Rectangle;
    }

    private void OnEllipseModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Ellipse;
    }

    private void OnTextModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Text;
    }

    private void OnPixelateModeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMode = SnippingToolMode.Pixelate;
    }

    private void OnThickness2Click(object sender, RoutedEventArgs e) => SetThickness(2.0);
    private void OnThickness4Click(object sender, RoutedEventArgs e) => SetThickness(4.0);
    private void OnThickness8Click(object sender, RoutedEventArgs e) => SetThickness(8.0);
    private void OnThickness14Click(object sender, RoutedEventArgs e) => SetThickness(14.0);

    private void SetThickness(double t)
    {
        if (ViewModel != null) ViewModel.StrokeThickness = t;
    }

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ColorOption option && ViewModel != null)
        {
            ViewModel.SelectedColor = option.Color;
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => ViewModel?.Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => ViewModel?.Redo();
    private void OnResetClick(object sender, RoutedEventArgs e) => ViewModel?.ResetAll();

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        _userZoomed = true;
        ZoomAroundViewportPoint(() => ViewModel?.ZoomIn(), GetViewportCenter());
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        _userZoomed = true;
        ZoomAroundViewportPoint(() => ViewModel?.ZoomOut(), GetViewportCenter());
    }

    private void OnZoom100Click(object sender, RoutedEventArgs e)
    {
        _userZoomed = true;
        ZoomAroundViewportPoint(() => ViewModel?.Zoom100(), GetViewportCenter());
    }

    private void OnZoomFitClick(object sender, RoutedEventArgs e)
    {
        _userZoomed = false;
        FitToViewport();
    }

    private Point GetViewportCenter()
    {
        return new Point(
            Math.Max(0, CanvasScrollViewer.ViewportWidth) / 2.0,
            Math.Max(0, CanvasScrollViewer.ViewportHeight) / 2.0);
    }

    /// <summary>
    /// Zooms while keeping the content point under <paramref name="viewportPoint"/>
    /// pinned in place, so the view doesn't jump toward the top-left.
    /// </summary>
    private void ZoomAroundViewportPoint(Action zoomAction, Point viewportPoint)
    {
        if (ViewModel == null)
        {
            zoomAction();
            return;
        }

        double oldZoom = ViewModel.ZoomScale;

        // Remember which image pixel is currently under the anchor point.
        var toImage = CanvasScrollViewer.TransformToDescendant(CanvasHostGrid);
        if (toImage == null)
        {
            zoomAction();
            return;
        }

        Point imagePoint = toImage.Transform(viewportPoint);

        zoomAction();

        if (Math.Abs(ViewModel.ZoomScale - oldZoom) < 0.0001) return;

        // Apply the new scale, then scroll so that same pixel sits under the anchor again.
        CanvasScrollViewer.UpdateLayout();

        Point newViewportPoint = CanvasHostGrid.TransformToAncestor(CanvasScrollViewer).Transform(imagePoint);
        Vector delta = newViewportPoint - viewportPoint;

        CanvasScrollViewer.ScrollToHorizontalOffset(CanvasScrollViewer.HorizontalOffset + delta.X);
        CanvasScrollViewer.ScrollToVerticalOffset(CanvasScrollViewer.VerticalOffset + delta.Y);
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            _userZoomed = true;

            Point anchor = e.GetPosition(CanvasScrollViewer);
            ZoomAroundViewportPoint(() =>
            {
                if (e.Delta > 0)
                    ViewModel?.ZoomIn();
                else
                    ViewModel?.ZoomOut();
            }, anchor);
        }
    }

    private void OnCropRectChanged(Rect cropRect)
    {
        if (cropRect.Width > 5 && cropRect.Height > 5)
        {
            CropDimensionsText.Text = $"Crop: {(int)cropRect.Width} × {(int)cropRect.Height} px";
            FloatingCropBar.Visibility = Visibility.Visible;
        }
        else
        {
            FloatingCropBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnConfirmCropClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && OverlayCanvas.CurrentCropRect.Width > 5)
        {
            ViewModel.ApplyCrop(OverlayCanvas.CurrentCropRect);
            OverlayCanvas.ClearCropSelection();
            FloatingCropBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancelCropClick(object sender, RoutedEventArgs e)
    {
        OverlayCanvas.ClearCropSelection();
        FloatingCropBar.Visibility = Visibility.Collapsed;
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel?.SaveAsAsync();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel?.CopyToClipboardAsync();
    }

    private void OnApplyToPopupClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplyToPopup();
    }

    private async void OnQuickSaveDestinationClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.DataContext is DestinationFolder folder && ViewModel != null)
        {
            await ViewModel.SaveToDestinationAsync(folder);
        }
    }
}
