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
