using System.IO;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class CaptureDetectionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly ImageCaptureService _captureService;
    private readonly CaptureDetectionService _detectionService;

    public CaptureDetectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_DetectTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
        _settingsService = new SettingsService(_logger, Path.Combine(_tempDir, "settings.json"));
        _captureService = new ImageCaptureService(_logger);
        _detectionService = new CaptureDetectionService(_settingsService, _captureService, _logger);
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

    [Fact]
    public void PauseAndResume_TogglesPausedState()
    {
        Assert.False(_detectionService.IsPaused);

        _detectionService.Pause();
        Assert.True(_detectionService.IsPaused);

        _detectionService.Resume();
        Assert.False(_detectionService.IsPaused);
    }

    [Fact]
    public void SuppressNextUpdate_SetsSuppressionWindow()
    {
        _detectionService.SuppressNextUpdate(TimeSpan.FromSeconds(5));

        // When notified while suppressed, ScreenshotDetected event is not raised
        bool eventFired = false;
        _detectionService.ScreenshotDetected += (s, candidate) => eventFired = true;

        _detectionService.NotifyClipboardUpdate();

        // Immediate check: no event fired
        Assert.False(eventFired);
    }

    [Fact]
    public async Task Settings_DetectionMode_ConfiguresConservativeVsFallback()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        Assert.Equal(ClipboardDetectionMode.Conservative, settings.ClipboardDetectionMode);
        Assert.False(settings.GenericImageFallbackEnabled);

        settings.GenericImageFallbackEnabled = true;
        settings.ClipboardDetectionMode = ClipboardDetectionMode.GenericFallback;
        await _settingsService.SaveSettingsAsync(settings);

        var reloaded = await _settingsService.LoadSettingsAsync();
        Assert.True(reloaded.GenericImageFallbackEnabled);
        Assert.Equal(ClipboardDetectionMode.GenericFallback, reloaded.ClipboardDetectionMode);
    }

    [Fact]
    public void ClassifyClipboardFormats_RejectsClipboardHistoryRestore()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };
        var formats = new[]
        {
            "Bitmap",
            "System.Drawing.Bitmap",
            "CanIncludeInClipboardHistory",
            "ExcludeClipboardContentFromMonitorProcessing",
            "ClipboardHistoryItemId"
        };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings);

        Assert.False(result.isCandidate);
        Assert.False(result.isProven);
        Assert.Contains("clipboard history", result.reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyClipboardFormats_RejectsClipboardHistoryRestoreInGenericMode()
    {
        var settings = new AppSettings
        {
            ClipboardDetectionMode = ClipboardDetectionMode.GenericFallback,
            GenericImageFallbackEnabled = true
        };
        var formats = new[] { "Bitmap", "ClipboardHistoryItemId" };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings);

        Assert.False(result.isCandidate);
        Assert.False(result.isProven);
    }

    [Fact]
    public void ClassifyClipboardFormats_AcceptsFreshScreenshot()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };
        var formats = new[] { "Bitmap", "System.Drawing.Bitmap", "CanIncludeInClipboardHistory" };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings);

        Assert.True(result.isCandidate);
        Assert.True(result.isProven);
        Assert.Contains("CanIncludeInClipboardHistory", result.reason);
    }

    [Fact]
    public void ClassifyClipboardFormats_AcceptsCleanStandaloneDib()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };
        var formats = new[] { "Bitmap", "DeviceIndependentBitmap" };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings);

        Assert.True(result.isCandidate);
        Assert.True(result.isProven);
        Assert.Contains("Standalone bitmap", result.reason);
    }

    [Fact]
    public void ClassifyClipboardFormats_RejectsImageCopiedFromPdfReader()
    {
        // Observed with Foxit PhantomPDF: a copied PDF image is published as a bare
        // DIB with no HTML/RTF/OLE markers, so only the owning process identifies it.
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };
        var formats = new[] { "Bitmap", "DeviceIndependentBitmap", "Format17", "System.Drawing.Bitmap" };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings, "FoxitPhantomPDF");

        Assert.False(result.isCandidate);
        Assert.False(result.isProven);
        Assert.Contains("FoxitPhantomPDF", result.reason);
    }

    [Fact]
    public void ClassifyClipboardFormats_RejectsImageCopiedFromWord()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };
        var formats = new[] { "Bitmap", "DeviceIndependentBitmap" };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            formats, hasFileDrop: false, hasImageFormat: true, settings, "WINWORD.exe");

        Assert.False(result.isCandidate);
        Assert.False(result.isProven);
    }

    [Fact]
    public void ClassifyClipboardFormats_RejectsMarkerFreeBitmapFromBrowser()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            new[] { "Bitmap", "DeviceIndependentBitmap" },
            hasFileDrop: false, hasImageFormat: true, settings, "chrome");

        Assert.False(result.isCandidate);
    }

    [Fact]
    public void ClassifyClipboardFormats_AcceptsCleanDibFromSnippingToolSource()
    {
        var settings = new AppSettings { ClipboardDetectionMode = ClipboardDetectionMode.Conservative };

        var result = CaptureDetectionService.ClassifyClipboardFormats(
            new[] { "Bitmap", "DeviceIndependentBitmap" },
            hasFileDrop: false, hasImageFormat: true, settings, "SnippingTool");

        Assert.True(result.isCandidate);
        Assert.True(result.isProven);
        Assert.Contains("source: SnippingTool", result.reason);
    }

    [Theory]
    [InlineData("FoxitPhantomPDF", true)]
    [InlineData("FoxitPhantomPDF.exe", true)]
    [InlineData(@"C:\Program Files\Foxit\FoxitPhantomPDF.exe", true)]
    [InlineData("WINWORD", true)]
    [InlineData("AcroRd32.exe", true)]
    [InlineData("msedge.exe", true)]
    [InlineData("SnippingTool", false)]
    [InlineData("ScreenClippingHost", false)]
    [InlineData("ShareX", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDocumentOrBrowserSource_ClassifiesProcessNames(string? processName, bool expected)
    {
        Assert.Equal(expected, CaptureDetectionService.IsDocumentOrBrowserSource(processName));
    }

    [Fact]
    public void IsForegroundMarkupEditor_HonorsOverrideAndRecognizesEditors()
    {
        try
        {
            ScreenshotDirector.Platform.NativeMethods.IsForegroundMarkupEditorOverride = () => true;
            Assert.True(ScreenshotDirector.Platform.NativeMethods.IsForegroundMarkupEditor());

            ScreenshotDirector.Platform.NativeMethods.IsForegroundMarkupEditorOverride = () => false;
            Assert.False(ScreenshotDirector.Platform.NativeMethods.IsForegroundMarkupEditor());
        }
        finally
        {
            ScreenshotDirector.Platform.NativeMethods.IsForegroundMarkupEditorOverride = null;
        }
    }
}
