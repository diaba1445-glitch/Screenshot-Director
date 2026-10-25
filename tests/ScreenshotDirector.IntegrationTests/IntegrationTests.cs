using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using Xunit;

namespace ScreenshotDirector.IntegrationTests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly ImageSaveService _saveService;

    public IntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_IntegTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
        _settingsService = new SettingsService(_logger, Path.Combine(_tempDir, "settings.json"));
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

    private static BitmapSource CreateTestBitmap(int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        Array.Fill(pixels, (byte)128);
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public async Task EndToEnd_SaveFlow_CreatesValidPngWithNoOverwrites()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        var destFolder = Path.Combine(_tempDir, "Screenshots");
        Directory.CreateDirectory(destFolder);

        var bmp = CreateTestBitmap(800, 600);
        string filename = "Capture.png";

        // First Save
        var res1 = await _saveService.SaveImageAsync(bmp, destFolder, filename, false);
        Assert.True(res1.Success);
        Assert.True(File.Exists(res1.SavedFilePath));

        // Second Save with identical name -> must collision-resolve to Capture (1).png
        var res2 = await _saveService.SaveImageAsync(bmp, destFolder, filename, false);
        Assert.True(res2.Success);
        Assert.True(File.Exists(res2.SavedFilePath));
        Assert.NotEqual(res1.SavedFilePath, res2.SavedFilePath);
        Assert.Contains("(1)", res2.SavedFilePath);

        // Third Save -> Capture (2).png
        var res3 = await _saveService.SaveImageAsync(bmp, destFolder, filename, false);
        Assert.True(res3.Success);
        Assert.Contains("(2)", res3.SavedFilePath);
    }

    [Fact]
    public async Task Reliability_Large4KScreenshot_EncodesAndSavesSuccessfully()
    {
        // 4K resolution (3840 x 2160)
        var largeBmp = CreateTestBitmap(3840, 2160);
        var saveFolder = Path.Combine(_tempDir, "4KTest");

        var result = await _saveService.SaveImageAsync(largeBmp, saveFolder, "4k_screenshot.png", false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.SavedFilePath);
        Assert.True(File.Exists(result.SavedFilePath));

        // Verify valid PNG header and dimensions
        using var stream = File.OpenRead(result.SavedFilePath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(3840, decoder.Frames[0].PixelWidth);
        Assert.Equal(2160, decoder.Frames[0].PixelHeight);
    }

    [Fact]
    public async Task Reliability_RepeatedSaves_NoLeakOrResourceLocks()
    {
        var bmp = CreateTestBitmap(200, 200);
        var saveFolder = Path.Combine(_tempDir, "LoopTest");

        for (int i = 0; i < 15; i++)
        {
            var res = await _saveService.SaveImageAsync(bmp, saveFolder, $"batch_{i}.png", false);
            Assert.True(res.Success);
        }

        var files = Directory.GetFiles(saveFolder, "*.png");
        Assert.Equal(15, files.Length);
    }
}
