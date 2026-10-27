using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using ScreenshotDirector.ViewModels;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class SnippingToolViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly ImageSaveService _saveService;
    private readonly NotificationService _notificationService;

    public SnippingToolViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_SnipTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
        _settingsService = new SettingsService(_logger, Path.Combine(_tempDir, "settings.json"));
        _saveService = new ImageSaveService(_logger);
        _notificationService = new NotificationService(_logger);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private static BitmapSource CreateTestBitmap(int width = 64, int height = 64)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;     // B
            pixels[i + 1] = 100; // G
            pixels[i + 2] = 50;  // R
            pixels[i + 3] = 255; // A
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource CreatePatternedBitmap(int width, int height, double dpi = 96)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = (byte)((x * 13 + y * 7) % 256);      // B
                pixels[i + 1] = (byte)((x * 5 + y * 17) % 256);  // G
                pixels[i + 2] = (byte)((x * 19 + y * 3) % 256);  // R
                pixels[i + 3] = 255;                              // A
            }
        }

        var bmp = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] GetPixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    [Fact]
    public void Initialize_SetsCorrectDefaults()
    {
        StaTestHelper.RunInSta(() =>
        {
            var testBmp = CreateTestBitmap(80, 60);
            var vm = new SnippingToolViewModel(testBmp, _saveService, _notificationService, _settingsService, null, _logger);

            Assert.Equal(80, vm.ImageWidth);
            Assert.Equal(60, vm.ImageHeight);
            Assert.Equal("80 × 60 px", vm.ResolutionText);
            Assert.Equal(SnippingToolMode.Pen, vm.ActiveMode);
            Assert.True(vm.IsPenMode);
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);
            Assert.Equal(1.0, vm.ZoomScale);
            Assert.Empty(vm.Annotations);
        });
    }

    [Fact]
    public void ToolMode_Switching_UpdatesProperties()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap());

            vm.ActiveMode = SnippingToolMode.Select;
            Assert.True(vm.IsSelectMode);
            Assert.False(vm.IsPenMode);

            vm.ActiveMode = SnippingToolMode.Highlighter;
            Assert.True(vm.IsHighlighterMode);

            vm.ActiveMode = SnippingToolMode.Arrow;
            Assert.True(vm.IsArrowMode);

            vm.ActiveMode = SnippingToolMode.Rectangle;
            Assert.True(vm.IsRectangleMode);

            vm.ActiveMode = SnippingToolMode.Ellipse;
            Assert.True(vm.IsEllipseMode);

            vm.ActiveMode = SnippingToolMode.Text;
            Assert.True(vm.IsTextMode);

            vm.ActiveMode = SnippingToolMode.Pixelate;
            Assert.True(vm.IsPixelateMode);
        });
    }

    [Fact]
    public void AddAnnotation_UpdatesUndoStack()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap());

            var stroke = new PenStrokeAnnotation
            {
                Points = new List<Point> { new Point(5, 5), new Point(10, 10) },
                StrokeColor = Colors.Red,
                Thickness = 4.0
            };

            vm.AddAnnotation(stroke);

            Assert.Single(vm.Annotations);
            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);
        });
    }

    [Fact]
    public void UndoAndRedo_RestoresState()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap());

            var stroke = new PenStrokeAnnotation
            {
                Points = new List<Point> { new Point(5, 5), new Point(10, 10) },
                StrokeColor = Colors.Red,
                Thickness = 4.0
            };

            vm.AddAnnotation(stroke);
            Assert.Single(vm.Annotations);

            vm.Undo();
            Assert.Empty(vm.Annotations);
            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            vm.Redo();
            Assert.Single(vm.Annotations);
            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);
        });
    }

    [Fact]
    public void ApplyCrop_ResizesImage_AndCanUndo()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap(100, 80));

            vm.ApplyCrop(new Rect(10, 10, 40, 30));

            Assert.Equal(40, vm.ImageWidth);
            Assert.Equal(30, vm.ImageHeight);
            Assert.True(vm.CanUndo);

            vm.Undo();

            Assert.Equal(100, vm.ImageWidth);
            Assert.Equal(80, vm.ImageHeight);
        });
    }

    [Fact]
    public void ResetAll_RevertsToOriginal()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap(100, 80));

            vm.ApplyCrop(new Rect(10, 10, 40, 30));
            vm.AddAnnotation(new ShapeAnnotation
            {
                ShapeType = SnippingToolMode.Rectangle,
                StartPoint = new Point(2, 2),
                EndPoint = new Point(20, 20),
                StrokeColor = Colors.Yellow,
                Thickness = 2.0
            });

            Assert.Equal(40, vm.ImageWidth);
            Assert.Single(vm.Annotations);

            vm.ResetAll();

            Assert.Equal(100, vm.ImageWidth);
            Assert.Equal(80, vm.ImageHeight);
            Assert.Empty(vm.Annotations);
        });
    }

    [Fact]
    public void RenderCompositeBitmap_ReturnsValidBitmap()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap(50, 50));
            vm.AddAnnotation(new ShapeAnnotation
            {
                ShapeType = SnippingToolMode.Arrow,
                StartPoint = new Point(5, 5),
                EndPoint = new Point(45, 45),
                StrokeColor = Colors.Cyan,
                Thickness = 3.0
            });

            var composite = vm.RenderCompositeBitmap();

            Assert.NotNull(composite);
            Assert.Equal(50, composite.PixelWidth);
            Assert.Equal(50, composite.PixelHeight);
        });
    }

    [Fact]
    public void RenderCompositeBitmap_WithoutAnnotations_ReturnsOriginalWithoutRerender()
    {
        StaTestHelper.RunInSta(() =>
        {
            var original = CreatePatternedBitmap(37, 29, 144);
            var vm = new SnippingToolViewModel(original, _saveService, _notificationService, _settingsService, null, _logger);

            var result = vm.RenderCompositeBitmap();

            // No markup means there is nothing to render: the exact source
            // object (pixels, DPI, color context) should flow straight through.
            Assert.Same(original, result);
            Assert.Equal(144.0, result.DpiX);
            Assert.Equal(144.0, result.DpiY);
            Assert.Equal(GetPixel(original, 12, 7), GetPixel(result, 12, 7));
        });
    }

    [Fact]
    public void RenderCompositeBitmap_WithAnnotations_PreservesDpiAndUntouchedPixels()
    {
        StaTestHelper.RunInSta(() =>
        {
            var original = CreatePatternedBitmap(40, 30, 144);
            var vm = new SnippingToolViewModel(original, _saveService, _notificationService, _settingsService, null, _logger);
            vm.AddAnnotation(new ShapeAnnotation
            {
                ShapeType = SnippingToolMode.Rectangle,
                StartPoint = new Point(2, 2),
                EndPoint = new Point(8, 8),
                StrokeColor = Colors.Red,
                Thickness = 1.0
            });

            var result = vm.RenderCompositeBitmap();

            Assert.NotSame(original, result);
            Assert.Equal(40, result.PixelWidth);
            Assert.Equal(30, result.PixelHeight);
            Assert.Equal(144.0, result.DpiX);
            Assert.Equal(144.0, result.DpiY);

            // Every pixel outside the annotation must survive the render unchanged.
            int stride = original.PixelWidth * 4;
            var originalPixels = new byte[stride * original.PixelHeight];
            var resultPixels = new byte[stride * result.PixelHeight];
            original.CopyPixels(originalPixels, stride, 0);
            result.CopyPixels(resultPixels, stride, 0);

            for (int y = 0; y < original.PixelHeight; y++)
            {
                for (int x = 0; x < original.PixelWidth; x++)
                {
                    if (x <= 10 && y <= 10) continue; // Rectangle annotation area

                    int i = y * stride + x * 4;
                    Assert.Equal(originalPixels[i], resultPixels[i]);
                    Assert.Equal(originalPixels[i + 1], resultPixels[i + 1]);
                    Assert.Equal(originalPixels[i + 2], resultPixels[i + 2]);
                    Assert.Equal(originalPixels[i + 3], resultPixels[i + 3]);
                }
            }
        });
    }

    [Fact]
    public void ApplyCrop_WithoutAnnotations_PreservesPixelsAndDpi()
    {
        StaTestHelper.RunInSta(() =>
        {
            var original = CreatePatternedBitmap(100, 80, 144);
            var vm = new SnippingToolViewModel(original, _saveService, _notificationService, _settingsService, null, _logger);

            vm.ApplyCrop(new Rect(10, 10, 40, 30));

            Assert.Equal(40, vm.ImageWidth);
            Assert.Equal(30, vm.ImageHeight);
            Assert.Equal(144.0, vm.CurrentBaseImage.DpiX);
            Assert.Equal(144.0, vm.CurrentBaseImage.DpiY);
            Assert.Equal(GetPixel(original, 10, 10), GetPixel(vm.CurrentBaseImage, 0, 0));
            Assert.Equal(GetPixel(original, 49, 39), GetPixel(vm.CurrentBaseImage, 39, 29));

            // Cropping must copy pixels 1:1, not resample them.
            var originalPixels = new byte[original.PixelWidth * original.PixelHeight * 4];
            original.CopyPixels(originalPixels, original.PixelWidth * 4, 0);
            var croppedPixels = new byte[vm.CurrentBaseImage.PixelWidth * vm.CurrentBaseImage.PixelHeight * 4];
            vm.CurrentBaseImage.CopyPixels(croppedPixels, vm.CurrentBaseImage.PixelWidth * 4, 0);

            for (int y = 0; y < 30; y++)
            {
                for (int x = 0; x < 40; x++)
                {
                    int source = ((y + 10) * original.PixelWidth + (x + 10)) * 4;
                    int target = (y * 40 + x) * 4;
                    Assert.Equal(originalPixels[source], croppedPixels[target]);
                    Assert.Equal(originalPixels[source + 1], croppedPixels[target + 1]);
                    Assert.Equal(originalPixels[source + 2], croppedPixels[target + 2]);
                    Assert.Equal(originalPixels[source + 3], croppedPixels[target + 3]);
                }
            }
        });
    }

    [Fact]
    public void SelectedColor_UpdatesSwatchSelectionFlags()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new SnippingToolViewModel(CreateTestBitmap());

            var red = vm.AvailableColors.Single(c => c.Name == "Red");
            Assert.True(red.IsSelected);
            Assert.Single(vm.AvailableColors, c => c.IsSelected);

            var green = vm.AvailableColors.Single(c => c.Name == "Green");
            vm.SelectedColor = green.Color;

            Assert.True(green.IsSelected);
            Assert.False(red.IsSelected);
            Assert.Single(vm.AvailableColors, c => c.IsSelected);
        });
    }

    [Fact]
    public void CapturePopupViewModel_UpdatePreviewImage_UpdatesPreviewAndResolution()
    {
        StaTestHelper.RunInSta(() =>
        {
            var popupVm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            popupVm.Initialize(new CaptureCandidate
            {
                Generation = 1,
                ImageSource = CreateTestBitmap(64, 64)
            });

            Assert.Equal("64 × 64", popupVm.ResolutionText);

            var editedBmp = CreateTestBitmap(120, 90);
            popupVm.UpdatePreviewImage(editedBmp);

            Assert.Same(editedBmp, popupVm.PreviewImage);
            Assert.Equal("120 × 90", popupVm.ResolutionText);
        });
    }

    [Fact]
    public void CapturePopupViewModel_IsSnippingToolOpen_PausesTimer()
    {
        StaTestHelper.RunInSta(() =>
        {
            var popupVm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            popupVm.Initialize(new CaptureCandidate
            {
                Generation = 1,
                ImageSource = CreateTestBitmap(64, 64)
            });

            Assert.False(popupVm.IsTimerPaused);

            popupVm.IsSnippingToolOpen = true;
            Assert.True(popupVm.IsTimerPaused);

            popupVm.IsSnippingToolOpen = false;
            Assert.False(popupVm.IsTimerPaused);
        });
    }
}
