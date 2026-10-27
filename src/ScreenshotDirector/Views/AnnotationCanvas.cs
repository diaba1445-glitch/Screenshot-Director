using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public class AnnotationCanvas : Canvas
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(SnippingToolViewModel), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewModelChanged));

    public SnippingToolViewModel? ViewModel
    {
        get => (SnippingToolViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnnotationCanvas canvas)
        {
            if (e.OldValue is SnippingToolViewModel oldVm)
            {
                oldVm.PropertyChanged -= canvas.OnViewModelPropertyChanged;
            }
            if (e.NewValue is SnippingToolViewModel newVm)
            {
                newVm.PropertyChanged += canvas.OnViewModelPropertyChanged;
                canvas.Width = newVm.ImageWidth;
                canvas.Height = newVm.ImageHeight;
            }
            canvas.InvalidateVisual();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SnippingToolViewModel.CurrentBaseImage) ||
            e.PropertyName == nameof(SnippingToolViewModel.ImageWidth) ||
            e.PropertyName == nameof(SnippingToolViewModel.ImageHeight))
        {
            if (ViewModel != null)
            {
                Width = ViewModel.ImageWidth;
                Height = ViewModel.ImageHeight;
            }
            CurrentCropRect = Rect.Empty;
        }

        InvalidateVisual();
    }

    private Point _startPoint;
    private Point _currentPoint;
    private bool _isDrawing;
    private readonly List<Point> _currentStrokePoints = new();
    private TextBox? _activeTextBox;
    private Point _textPosition;

    public Rect CurrentCropRect { get; set; } = Rect.Empty;
    public event Action<Rect>? CropRectChanged;

    public AnnotationCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (ViewModel == null) return;

        // If a text box is open and user clicks outside, commit it
        if (_activeTextBox != null)
        {
            CommitActiveTextBox();
            return;
        }

        _startPoint = e.GetPosition(this);
        _currentPoint = _startPoint;

        if (ViewModel.ActiveMode == SnippingToolMode.Text)
        {
            ShowInlineTextBox(_startPoint);
            return;
        }

        _isDrawing = true;
        CaptureMouse();

        if (ViewModel.ActiveMode == SnippingToolMode.Pen || ViewModel.ActiveMode == SnippingToolMode.Highlighter)
        {
            _currentStrokePoints.Clear();
            _currentStrokePoints.Add(_startPoint);
        }
        else if (ViewModel.ActiveMode == SnippingToolMode.Select)
        {
            CurrentCropRect = new Rect(_startPoint, _startPoint);
            CropRectChanged?.Invoke(CurrentCropRect);
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDrawing || ViewModel == null) return;

        _currentPoint = e.GetPosition(this);

        // Clamp points to image dimensions
        _currentPoint = new Point(
            Math.Clamp(_currentPoint.X, 0, ViewModel.ImageWidth),
            Math.Clamp(_currentPoint.Y, 0, ViewModel.ImageHeight));

        if (ViewModel.ActiveMode == SnippingToolMode.Pen || ViewModel.ActiveMode == SnippingToolMode.Highlighter)
        {
            _currentStrokePoints.Add(_currentPoint);
        }
        else if (ViewModel.ActiveMode == SnippingToolMode.Select)
        {
            CurrentCropRect = new Rect(_startPoint, _currentPoint);
            CropRectChanged?.Invoke(CurrentCropRect);
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_isDrawing || ViewModel == null) return;

        _isDrawing = false;
        ReleaseMouseCapture();

        _currentPoint = e.GetPosition(this);
        _currentPoint = new Point(
            Math.Clamp(_currentPoint.X, 0, ViewModel.ImageWidth),
            Math.Clamp(_currentPoint.Y, 0, ViewModel.ImageHeight));

        switch (ViewModel.ActiveMode)
        {
            case SnippingToolMode.Pen:
            case SnippingToolMode.Highlighter:
                if (_currentStrokePoints.Count >= 2)
                {
                    ViewModel.AddAnnotation(new PenStrokeAnnotation
                    {
                        Points = new List<Point>(_currentStrokePoints),
                        StrokeColor = ViewModel.SelectedColor,
                        Thickness = ViewModel.StrokeThickness,
                        IsHighlighter = ViewModel.IsHighlighterMode
                    });
                }
                _currentStrokePoints.Clear();
                break;

            case SnippingToolMode.Rectangle:
            case SnippingToolMode.Ellipse:
            case SnippingToolMode.Arrow:
                var shapeRect = new Rect(_startPoint, _currentPoint);
                if (shapeRect.Width > 2 || shapeRect.Height > 2)
                {
                    ViewModel.AddAnnotation(new ShapeAnnotation
                    {
                        ShapeType = ViewModel.ActiveMode,
                        StartPoint = _startPoint,
                        EndPoint = _currentPoint,
                        StrokeColor = ViewModel.SelectedColor,
                        Thickness = ViewModel.StrokeThickness
                    });
                }
                break;

            case SnippingToolMode.Pixelate:
                var redactRect = new Rect(_startPoint, _currentPoint);
                if (redactRect.Width >= 4 && redactRect.Height >= 4)
                {
                    var patch = PixelateHelper.CreatePixelatedPatch(ViewModel.CurrentBaseImage, redactRect);
                    if (patch != null)
                    {
                        ViewModel.AddAnnotation(new PixelateAnnotation
                        {
                            Bounds = redactRect,
                            PixelatedBitmap = patch
                        });
                    }
                }
                break;

            case SnippingToolMode.Select:
                CurrentCropRect = new Rect(_startPoint, _currentPoint);
                CropRectChanged?.Invoke(CurrentCropRect);
                break;
        }

        InvalidateVisual();
    }

    private void ShowInlineTextBox(Point position)
    {
        _textPosition = position;
        _activeTextBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 18, 30)),
            Foreground = new SolidColorBrush(ViewModel?.SelectedColor ?? Colors.White),
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = ViewModel?.FontSize ?? 18,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 120,
            AcceptsReturn = false
        };

        Canvas.SetLeft(_activeTextBox, position.X);
        Canvas.SetTop(_activeTextBox, position.Y);

        _activeTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitActiveTextBox();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelActiveTextBox();
            }
        };

        _activeTextBox.LostFocus += (s, e) => CommitActiveTextBox();

        Children.Add(_activeTextBox);
        _activeTextBox.Focus();
    }

    private void CommitActiveTextBox()
    {
        if (_activeTextBox == null || ViewModel == null) return;

        string text = _activeTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            ViewModel.AddAnnotation(new TextAnnotation
            {
                Position = _textPosition,
                Text = text,
                TextColor = ViewModel.SelectedColor,
                FontSize = ViewModel.FontSize
            });
        }

        Children.Remove(_activeTextBox);
        _activeTextBox = null;
        InvalidateVisual();
    }

    private void CancelActiveTextBox()
    {
        if (_activeTextBox != null)
        {
            Children.Remove(_activeTextBox);
            _activeTextBox = null;
            InvalidateVisual();
        }
    }

    public void ClearCropSelection()
    {
        CurrentCropRect = Rect.Empty;
        CropRectChanged?.Invoke(CurrentCropRect);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ViewModel == null) return;

        // 1. Draw all committed annotations
        foreach (var item in ViewModel.Annotations)
        {
            item.Draw(dc);
        }

        // 2. Draw live transient in-progress annotation
        if (_isDrawing)
        {
            switch (ViewModel.ActiveMode)
            {
                case SnippingToolMode.Pen:
                case SnippingToolMode.Highlighter:
                    if (_currentStrokePoints.Count >= 2)
                    {
                        var stroke = new PenStrokeAnnotation
                        {
                            Points = new List<Point>(_currentStrokePoints),
                            StrokeColor = ViewModel.SelectedColor,
                            Thickness = ViewModel.StrokeThickness,
                            IsHighlighter = ViewModel.IsHighlighterMode
                        };
                        stroke.Draw(dc);
                    }
                    break;

                case SnippingToolMode.Rectangle:
                case SnippingToolMode.Ellipse:
                case SnippingToolMode.Arrow:
                    var shape = new ShapeAnnotation
                    {
                        ShapeType = ViewModel.ActiveMode,
                        StartPoint = _startPoint,
                        EndPoint = _currentPoint,
                        StrokeColor = ViewModel.SelectedColor,
                        Thickness = ViewModel.StrokeThickness
                    };
                    shape.Draw(dc);
                    break;

                case SnippingToolMode.Pixelate:
                    var redactRect = new Rect(_startPoint, _currentPoint);
                    if (redactRect.Width > 0 && redactRect.Height > 0)
                    {
                        var previewBrush = new SolidColorBrush(Color.FromArgb(160, 60, 60, 75));
                        var previewPen = new Pen(new SolidColorBrush(Color.FromRgb(168, 85, 247)), 1.5)
                        {
                            DashStyle = DashStyles.Dash
                        };
                        previewBrush.Freeze();
                        previewPen.Freeze();
                        dc.DrawRectangle(previewBrush, previewPen, redactRect);
                    }
                    break;
            }
        }

        // 3. Draw crop overlay if in Select mode and a crop rect is set
        if (ViewModel.IsSelectMode && CurrentCropRect.Width > 2 && CurrentCropRect.Height > 2)
        {
            double imgW = ViewModel.ImageWidth;
            double imgH = ViewModel.ImageHeight;
            Rect crop = CurrentCropRect;

            var maskBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            maskBrush.Freeze();

            // Dim 4 regions around crop box
            if (crop.Y > 0)
                dc.DrawRectangle(maskBrush, null, new Rect(0, 0, imgW, Math.Max(0, crop.Y)));
            if (crop.Bottom < imgH)
                dc.DrawRectangle(maskBrush, null, new Rect(0, crop.Bottom, imgW, Math.Max(0, imgH - crop.Bottom)));
            if (crop.X > 0)
                dc.DrawRectangle(maskBrush, null, new Rect(0, crop.Y, Math.Max(0, crop.X), crop.Height));
            if (crop.Right < imgW)
                dc.DrawRectangle(maskBrush, null, new Rect(crop.Right, crop.Y, Math.Max(0, imgW - crop.Right), crop.Height));

            // Crop boundary
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(192, 132, 252)), 2)
            {
                DashStyle = DashStyles.Dash
            };
            borderPen.Freeze();
            dc.DrawRectangle(null, borderPen, crop);

            // Handle points
            var handleBrush = Brushes.White;
            var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(124, 58, 237)), 1.5);
            handlePen.Freeze();
            const double hs = 8.0;

            Point[] handles = new Point[]
            {
                new Point(crop.Left, crop.Top),
                new Point(crop.Right, crop.Top),
                new Point(crop.Left, crop.Bottom),
                new Point(crop.Right, crop.Bottom),
                new Point(crop.Left + crop.Width / 2, crop.Top),
                new Point(crop.Left + crop.Width / 2, crop.Bottom),
                new Point(crop.Left, crop.Top + crop.Height / 2),
                new Point(crop.Right, crop.Top + crop.Height / 2)
            };

            foreach (var pt in handles)
            {
                dc.DrawRectangle(handleBrush, handlePen, new Rect(pt.X - hs / 2, pt.Y - hs / 2, hs, hs));
            }
        }
    }
}
