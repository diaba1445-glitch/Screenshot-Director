using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.ViewModels;

public class ColorOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public Color Color { get; set; }
    public string Hex { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class SnippingToolViewModel : INotifyPropertyChanged
{
    private readonly IImageSaveService? _saveService;
    private readonly INotificationService? _notificationService;
    private readonly ISettingsService? _settingsService;
    private readonly ICaptureDetectionService? _detectionService;
    private readonly IAppLogger? _logger;

    private readonly BitmapSource _originalImage;
    private BitmapSource _currentBaseImage = null!;
    private SnippingToolMode _activeMode = SnippingToolMode.Pen;
    private Color _selectedColor = Color.FromRgb(239, 68, 68); // #EF4444 Red default
    private double _strokeThickness = 4.0;
    private double _fontSize = 18.0;
    private double _zoomScale = 1.0;
    private string _statusMessage = string.Empty;
    private bool _isStatusMessageVisible;
    private System.Windows.Threading.DispatcherTimer? _statusTimer;

    private readonly Stack<SnippingStateSnapshot> _undoStack = new();
    private readonly Stack<SnippingStateSnapshot> _redoStack = new();
    private readonly List<IAnnotationItem> _annotations = new();

    public event Action? RequestClose;
    public event Action<BitmapSource>? ApplyEditsRequested;
    public event Action? SavedDirectly;
    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapSource CurrentBaseImage
    {
        get => _currentBaseImage;
        private set
        {
            _currentBaseImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageWidth));
            OnPropertyChanged(nameof(ImageHeight));
            OnPropertyChanged(nameof(ResolutionText));
        }
    }

    public double ImageWidth => CurrentBaseImage.PixelWidth;
    public double ImageHeight => CurrentBaseImage.PixelHeight;

    public string ResolutionText => $"{CurrentBaseImage.PixelWidth} × {CurrentBaseImage.PixelHeight} px";

    public SnippingToolMode ActiveMode
    {
        get => _activeMode;
        set
        {
            if (_activeMode != value)
            {
                _activeMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelectMode));
                OnPropertyChanged(nameof(IsPenMode));
                OnPropertyChanged(nameof(IsHighlighterMode));
                OnPropertyChanged(nameof(IsArrowMode));
                OnPropertyChanged(nameof(IsRectangleMode));
                OnPropertyChanged(nameof(IsEllipseMode));
                OnPropertyChanged(nameof(IsTextMode));
                OnPropertyChanged(nameof(IsPixelateMode));
            }
        }
    }

    public bool IsSelectMode => ActiveMode == SnippingToolMode.Select;
    public bool IsPenMode => ActiveMode == SnippingToolMode.Pen;
    public bool IsHighlighterMode => ActiveMode == SnippingToolMode.Highlighter;
    public bool IsArrowMode => ActiveMode == SnippingToolMode.Arrow;
    public bool IsRectangleMode => ActiveMode == SnippingToolMode.Rectangle;
    public bool IsEllipseMode => ActiveMode == SnippingToolMode.Ellipse;
    public bool IsTextMode => ActiveMode == SnippingToolMode.Text;
    public bool IsPixelateMode => ActiveMode == SnippingToolMode.Pixelate;

    public Color SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (_selectedColor != value)
            {
                _selectedColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedColorBrush));
                SyncSelectedColorOption();
            }
        }
    }

    public SolidColorBrush SelectedColorBrush => new SolidColorBrush(SelectedColor);

    public double StrokeThickness
    {
        get => _strokeThickness;
        set
        {
            if (Math.Abs(_strokeThickness - value) > 0.01)
            {
                _strokeThickness = value;
                OnPropertyChanged();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) > 0.01)
            {
                _fontSize = value;
                OnPropertyChanged();
            }
        }
    }

    public double ZoomScale
    {
        get => _zoomScale;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 5.0);
            if (Math.Abs(_zoomScale - clamped) > 0.001)
            {
                _zoomScale = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ZoomPercentageText));
            }
        }
    }

    public string ZoomPercentageText => $"{(int)Math.Round(ZoomScale * 100)}%";

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsStatusMessageVisible
    {
        get => _isStatusMessageVisible;
        set { _isStatusMessageVisible = value; OnPropertyChanged(); }
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public IReadOnlyList<IAnnotationItem> Annotations => _annotations.AsReadOnly();

    public ObservableCollection<DestinationFolder> Destinations { get; } = new();

    public ObservableCollection<ColorOption> AvailableColors { get; } = new()
    {
        new ColorOption { Name = "Red", Color = Color.FromRgb(239, 68, 68), Hex = "#EF4444" },
        new ColorOption { Name = "Yellow", Color = Color.FromRgb(245, 158, 11), Hex = "#F59E0B" },
        new ColorOption { Name = "Green", Color = Color.FromRgb(16, 185, 129), Hex = "#10B981" },
        new ColorOption { Name = "Cyan", Color = Color.FromRgb(6, 182, 212), Hex = "#06B6D4" },
        new ColorOption { Name = "Blue", Color = Color.FromRgb(59, 130, 246), Hex = "#3B82F6" },
        new ColorOption { Name = "Purple", Color = Color.FromRgb(139, 92, 246), Hex = "#8B5CF6" },
        new ColorOption { Name = "White", Color = Color.FromRgb(255, 255, 255), Hex = "#FFFFFF" },
        new ColorOption { Name = "Black", Color = Color.FromRgb(24, 24, 27), Hex = "#18181B" }
    };

    public SnippingToolViewModel(
        BitmapSource sourceImage,
        IImageSaveService? saveService = null,
        INotificationService? notificationService = null,
        ISettingsService? settingsService = null,
        ICaptureDetectionService? detectionService = null,
        IAppLogger? logger = null)
    {
        _originalImage = sourceImage;
        CurrentBaseImage = sourceImage;
        _saveService = saveService;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _detectionService = detectionService;
        _logger = logger;

        if (_settingsService?.CurrentSettings?.Destinations != null)
        {
            foreach (var dest in _settingsService.CurrentSettings.Destinations.Where(d => d.IsEnabled).OrderBy(d => d.ShortcutNumber))
            {
                Destinations.Add(dest);
            }
        }

        SyncSelectedColorOption();
    }

    private void SyncSelectedColorOption()
    {
        foreach (var option in AvailableColors)
        {
            option.IsSelected = option.Color == _selectedColor;
        }
    }

    public void AddAnnotation(IAnnotationItem item)
    {
        PushUndoSnapshot();
        _annotations.Add(item);
        OnPropertyChanged(nameof(Annotations));
    }

    public void ApplyCrop(Rect cropRect)
    {
        int x = Math.Max(0, (int)cropRect.X);
        int y = Math.Max(0, (int)cropRect.Y);
        int w = Math.Min((int)cropRect.Width, CurrentBaseImage.PixelWidth - x);
        int h = Math.Min((int)cropRect.Height, CurrentBaseImage.PixelHeight - y);

        if (w < 10 || h < 10)
        {
            ShowStatus("Crop area too small (minimum 10×10).");
            return;
        }

        PushUndoSnapshot();

        try
        {
            // First render current visual state (base image + current annotations)
            var currentComposite = RenderCompositeBitmap();

            BitmapSource cropped = new CroppedBitmap(currentComposite, new Int32Rect(x, y, w, h));

            // Keep the existing pixel format when possible; a redundant format
            // conversion can introduce unnecessary copying/premultiplication.
            if (cropped.Format != PixelFormats.Bgra32 && cropped.Format != PixelFormats.Pbgra32)
            {
                cropped = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
            }

            cropped.Freeze();
            CurrentBaseImage = cropped;
            _annotations.Clear();
            OnPropertyChanged(nameof(Annotations));

            ShowStatus($"Cropped to {w} × {h} px");
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to apply crop", ex);
            ShowStatus("Failed to apply crop.");
        }
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var currentSnapshot = new SnippingStateSnapshot(CurrentBaseImage, _annotations);
        _redoStack.Push(currentSnapshot);

        var previousSnapshot = _undoStack.Pop();
        CurrentBaseImage = previousSnapshot.BaseImage;
        _annotations.Clear();
        _annotations.AddRange(previousSnapshot.Annotations);

        UpdateUndoRedoProperties();
        OnPropertyChanged(nameof(Annotations));
        ShowStatus("Undo");
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var currentSnapshot = new SnippingStateSnapshot(CurrentBaseImage, _annotations);
        _undoStack.Push(currentSnapshot);

        var nextSnapshot = _redoStack.Pop();
        CurrentBaseImage = nextSnapshot.BaseImage;
        _annotations.Clear();
        _annotations.AddRange(nextSnapshot.Annotations);

        UpdateUndoRedoProperties();
        OnPropertyChanged(nameof(Annotations));
        ShowStatus("Redo");
    }

    public void ResetAll()
    {
        if (_annotations.Count == 0 && ReferenceEquals(CurrentBaseImage, _originalImage))
        {
            return;
        }

        PushUndoSnapshot();
        CurrentBaseImage = _originalImage;
        _annotations.Clear();
        OnPropertyChanged(nameof(Annotations));
        ShowStatus("Reverted to original screenshot.");
    }

    private void PushUndoSnapshot()
    {
        _undoStack.Push(new SnippingStateSnapshot(CurrentBaseImage, _annotations));
        _redoStack.Clear();
        UpdateUndoRedoProperties();
    }

    private void UpdateUndoRedoProperties()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public BitmapSource RenderCompositeBitmap()
    {
        // With no markup there is nothing to composite. Return the original
        // bitmap object so its exact pixels, DPI and color profile survive the
        // save/copy/apply path instead of being re-rendered at 96 DPI.
        if (_annotations.Count == 0)
        {
            return CurrentBaseImage;
        }

        int width = CurrentBaseImage.PixelWidth;
        int height = CurrentBaseImage.PixelHeight;

        double dpiX = IsValidDpi(CurrentBaseImage.DpiX) ? CurrentBaseImage.DpiX : 96.0;
        double dpiY = IsValidDpi(CurrentBaseImage.DpiY) ? CurrentBaseImage.DpiY : 96.0;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            bool needsDpiScale = Math.Abs(dpiX - 96.0) > 0.01 || Math.Abs(dpiY - 96.0) > 0.01;
            if (needsDpiScale)
            {
                // Annotation coordinates are image pixels (1 DIP = 1 px at 96 DPI).
                // Compensate so a non-96-DPI render still maps one source pixel
                // to one output pixel while keeping the original DPI metadata.
                dc.PushTransform(new ScaleTransform(96.0 / dpiX, 96.0 / dpiY));
            }

            dc.DrawImage(CurrentBaseImage, new Rect(0, 0, width, height));

            foreach (var item in _annotations)
            {
                item.Draw(dc);
            }

            if (needsDpiScale)
            {
                dc.Pop();
            }
        }

        var rtb = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static bool IsValidDpi(double dpi) =>
        !double.IsNaN(dpi) && !double.IsInfinity(dpi) && dpi > 0.0;

    public void ApplyToPopup()
    {
        var finalImage = RenderCompositeBitmap();
        ApplyEditsRequested?.Invoke(finalImage);
        RequestClose?.Invoke();
    }

    public async Task CopyToClipboardAsync()
    {
        try
        {
            var finalImage = RenderCompositeBitmap();

            // Suppress clipboard listener to avoid triggering duplicate screenshot detection
            _detectionService?.SuppressNextUpdate(TimeSpan.FromSeconds(2));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Clipboard.SetImage(finalImage);
            });

            ShowStatus("✓ Copied to clipboard!");
            _logger?.Info("Snipped/edited screenshot copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to copy image to clipboard", ex);
            ShowStatus("Failed to copy image to clipboard.");
        }
    }

    public async Task<bool> SaveToDestinationAsync(DestinationFolder folder)
    {
        if (_saveService == null) return false;

        try
        {
            var finalImage = RenderCompositeBitmap();
            var settings = _settingsService?.CurrentSettings;
            string pattern = settings?.DefaultFilenamePattern ?? "Screenshot_{yyyy-MM-dd_HH-mm-ss}.png";
            string filename = _saveService.GenerateDefaultFilename(pattern);

            bool copyPath = settings?.CopySavedPathToClipboard ?? false;
            var result = await _saveService.SaveImageAsync(finalImage, folder.Path, filename, copyPath);

            if (result.Success)
            {
                string fileName = Path.GetFileName(result.SavedFilePath ?? filename);
                string message = result.CopiedPathToClipboard
                    ? $"Saved as {fileName} (Path copied to clipboard)"
                    : $"Saved as {fileName}";

                _notificationService?.ShowSuccess("Screenshot Saved", message, result.SavedFilePath);
                SavedDirectly?.Invoke();
                RequestClose?.Invoke();
                return true;
            }
            else
            {
                ShowStatus($"Save failed: {result.ErrorMessage}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Exception saving from snipping tool", ex);
            ShowStatus($"Save error: {ex.Message}");
            return false;
        }
    }

    public async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Snipped Screenshot As",
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
            DefaultExt = ".png",
            FileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var finalImage = RenderCompositeBitmap();
                string filePath = dialog.FileName;
                string ext = Path.GetExtension(filePath).ToLowerInvariant();

                await Task.Run(() =>
                {
                    BitmapEncoder encoder = ext == ".jpg" || ext == ".jpeg"
                        ? new JpegBitmapEncoder { QualityLevel = 95 }
                        : new PngBitmapEncoder();

                    encoder.Frames.Add(BitmapFrame.Create(finalImage));

                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    encoder.Save(fs);
                });

                _notificationService?.ShowSuccess("Screenshot Saved", $"Saved to {Path.GetFileName(filePath)}", filePath);
                ShowStatus($"Saved to {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed Save As", ex);
                ShowStatus($"Save failed: {ex.Message}");
            }
        }
    }

    public void ShowStatus(string message)
    {
        StatusMessage = message;
        IsStatusMessageVisible = true;

        _statusTimer?.Stop();
        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _statusTimer.Tick += (s, e) =>
        {
            _statusTimer.Stop();
            IsStatusMessageVisible = false;
        };
        _statusTimer.Start();
    }

    public void ZoomIn() => ZoomScale += 0.15;
    public void ZoomOut() => ZoomScale -= 0.15;
    public void Zoom100() => ZoomScale = 1.0;

    public void FitToWindow(double availableWidth, double availableHeight)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || ImageWidth <= 0 || ImageHeight <= 0)
        {
            ZoomScale = 1.0;
            return;
        }

        // Account for the canvas host's 24px margin on each side, the 1px
        // border, and a little breathing room so "Fit" never shows scrollbars.
        const double chrome = 56;
        double scaleX = (availableWidth - chrome) / ImageWidth;
        double scaleY = (availableHeight - chrome) / ImageHeight;
        double bestFit = Math.Min(scaleX, scaleY);

        ZoomScale = Math.Min(1.0, Math.Max(0.1, bestFit));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
