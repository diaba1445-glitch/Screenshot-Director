using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class SaveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly ImageSaveService _saveService;

    public SaveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
        _saveService = new ImageSaveService(_logger);
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

    private static BitmapSource CreateTestBitmap(int width = 100, int height = 80)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;   // B
            pixels[i + 1] = 0; // G
            pixels[i + 2] = 0; // R
            pixels[i + 3] = 255; // A
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void GenerateDefaultFilename_UsesLocalClockAndPngExtension()
    {
        string filename = _saveService.GenerateDefaultFilename("Screenshot_yyyy-MM-dd_HH-mm-ss.png");
        Assert.StartsWith("Screenshot_", filename);
        Assert.EndsWith(".png", filename);
    }

    [Theory]
    [InlineData("valid_name.png", true)]
    [InlineData("valid-image", true)]
    [InlineData("../traversal.png", false)]
    [InlineData("..\\traversal.png", false)]
    [InlineData("folder/image.png", false)]
    [InlineData("C:\\image.png", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("invalid*name.png", false)]
    public void ValidateFilename_ValidatesAndRejectsPathTraversal(string input, bool expectedValid)
    {
        bool isValid = _saveService.ValidateFilename(input, out string result);
        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.EndsWith(".png", result);
        }
    }

    [Fact]
    public void ResolveCollision_CreatesNumberedSuffix()
    {
        string baseName = "test.png";
        string filePath1 = Path.Combine(_tempDir, baseName);
        File.WriteAllText(filePath1, "data");

        string resolved1 = _saveService.ResolveCollision(_tempDir, baseName, CollisionPolicy.NumberedSuffix);
        Assert.Equal(Path.Combine(_tempDir, "test (1).png"), resolved1);

        File.WriteAllText(resolved1, "data");
        string resolved2 = _saveService.ResolveCollision(_tempDir, baseName, CollisionPolicy.NumberedSuffix);
        Assert.Equal(Path.Combine(_tempDir, "test (2).png"), resolved2);
    }

    [Fact]
    public async Task SaveImageAsync_SavesValidPngWithOriginalDimensions()
    {
        var bitmap = CreateTestBitmap(120, 90);
        string filename = "valid_test.png";

        var result = await _saveService.SaveImageAsync(bitmap, _tempDir, filename, copyPathToClipboard: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.SavedFilePath);
        Assert.True(File.Exists(result.SavedFilePath));

        // Read saved PNG and verify dimensions
        using var stream = File.OpenRead(result.SavedFilePath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        Assert.Equal(120, frame.PixelWidth);
        Assert.Equal(90, frame.PixelHeight);
    }

    [Fact]
    public async Task SaveImageAsync_ResolvesCollisionAutomatically()
    {
        var bitmap = CreateTestBitmap(50, 50);
        string filename = "collision_test.png";

        // Save first file
        var result1 = await _saveService.SaveImageAsync(bitmap, _tempDir, filename, copyPathToClipboard: false);
        Assert.True(result1.Success);

        // Save second file with same name
        var result2 = await _saveService.SaveImageAsync(bitmap, _tempDir, filename, copyPathToClipboard: false);
        Assert.True(result2.Success);

        Assert.NotEqual(result1.SavedFilePath, result2.SavedFilePath);
        Assert.Contains("(1)", result2.SavedFilePath);
        Assert.True(File.Exists(result1.SavedFilePath));
        Assert.True(File.Exists(result2.SavedFilePath));
    }

    [Fact]
    public async Task SaveImageAsync_InvalidFilename_FailsGracefully()
    {
        var bitmap = CreateTestBitmap(50, 50);
        var result = await _saveService.SaveImageAsync(bitmap, _tempDir, "../hacked.png", copyPathToClipboard: false);

        Assert.False(result.Success);
        Assert.Equal(SaveErrorType.InvalidFilename, result.ErrorType);
    }

    [Fact]
    public async Task SaveImageAsync_EmptyDestination_FailsGracefully()
    {
        var bitmap = CreateTestBitmap(50, 50);
        var result = await _saveService.SaveImageAsync(bitmap, "", "test.png", copyPathToClipboard: false);

        Assert.False(result.Success);
        Assert.Equal(SaveErrorType.DestinationNotFound, result.ErrorType);
    }

    [Fact]
    public async Task SaveImageAsync_NullImage_FailsGracefully()
    {
        var result = await _saveService.SaveImageAsync(null!, _tempDir, "test.png", copyPathToClipboard: false);

        Assert.False(result.Success);
        Assert.Equal(SaveErrorType.EncodingError, result.ErrorType);
    }
}
