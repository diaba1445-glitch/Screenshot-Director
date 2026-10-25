using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using ScreenshotDirector.ViewModels;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class PopupViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly ImageSaveService _saveService;
    private readonly NotificationService _notificationService;

    public PopupViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_VMTest_{Guid.NewGuid():N}");
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

    private static BitmapSource CreateTestBitmap()
    {
        int width = 32, height = 32, stride = 32 * 4;
        byte[] pixels = new byte[height * stride];
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void Initialize_PopulatesDestinationsAndShortcuts()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            var candidate = new CaptureCandidate
            {
                Generation = 1,
                ImageSource = CreateTestBitmap()
            };

            vm.Initialize(candidate);

            Assert.NotNull(vm.PreviewImage);
            Assert.NotEmpty(vm.Destinations);
            Assert.Equal(1, vm.Destinations[0].ShortcutNumber);
            Assert.StartsWith("Screenshot_", vm.Filename);
        });
    }

    [Fact]
    public void ResetTimeout_ResetsProgressToOne()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            vm.TimeoutProgress = 0.4;
            vm.ResetTimeout();

            Assert.Equal(1.0, vm.TimeoutProgress);
        });
    }

    [Fact]
    public void Dismiss_FiresRequestClose()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            bool closed = false;
            vm.RequestClose += (s, e) => closed = true;

            vm.Dismiss();
            Assert.True(closed);
        });
    }

    [Fact]
    public async Task HandleShortcutKeyAsync_ExecutesMatchingDestinationSave()
    {
        await StaTestHelper.RunInStaAsync(async () =>
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var targetDir = Path.Combine(_tempDir, "FolderOne");
            Directory.CreateDirectory(targetDir);

            settings.Destinations.Clear();
            settings.Destinations.Add(new DestinationFolder
            {
                Name = "Folder 1",
                Path = targetDir,
                ShortcutNumber = 1
            });
            await _settingsService.SaveSettingsAsync(settings);

            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            bool closed = false;
            vm.RequestClose += (s, e) => closed = true;

            bool handled = await vm.HandleShortcutKeyAsync(1);

            Assert.True(handled);
            Assert.True(closed);
            var savedFiles = Directory.GetFiles(targetDir, "*.png");
            Assert.Single(savedFiles);
        });
    }

    [Fact]
    public void FilenameChange_ResetsTimeoutProgress()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            vm.TimeoutProgress = 0.25;
            vm.Filename = "NewName.png";

            Assert.Equal(1.0, vm.TimeoutProgress);
        });
    }

    [Fact]
    public async Task SaveFailure_KeepsPopupOpen_AndSetsErrorMessage()
    {
        await StaTestHelper.RunInStaAsync(async () =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            bool closed = false;
            vm.RequestClose += (s, e) => closed = true;

            // Trigger save with an invalid destination
            var badFolder = new DestinationFolder { Name = "Bad", Path = "" };
            bool success = await vm.TriggerSaveAsync(badFolder);

            Assert.False(success);
            Assert.False(closed);
            Assert.True(vm.HasError);
            Assert.NotEmpty(vm.ErrorMessage);
        });
    }
}
