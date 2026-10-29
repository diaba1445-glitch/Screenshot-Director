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
            Assert.Equal(string.Empty, vm.Filename); // Empty by default per requirements
            Assert.StartsWith("Screenshot_", vm.DefaultFilename);
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
    public void FilenameChange_PausesTimer()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.False(vm.IsTimerPaused);
            vm.Filename = "NewCustomName";

            Assert.True(vm.IsTimerPaused);
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

    [Fact]
    public void IsNotSaving_InvertsIsSaving_AndNotifiesChanges()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            Assert.False(vm.IsSaving);
            Assert.True(vm.IsNotSaving);

            bool isNotSavingChanged = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.IsNotSaving))
                {
                    isNotSavingChanged = true;
                }
            };

            vm.IsSaving = true;
            Assert.False(vm.IsNotSaving);
            Assert.True(isNotSavingChanged);
        });
    }

    [Fact]
    public async Task HasNoDestinations_ReflectsEmptyState()
    {
        await StaTestHelper.RunInStaAsync(async () =>
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.Destinations.Clear();
            await _settingsService.SaveSettingsAsync(settings);

            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.True(vm.HasNoDestinations);
            Assert.Empty(vm.Destinations);
        });
    }

    [Fact]
    public void PopupViewModel_ResolutionText_And_Position_ComputedCorrectly()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            var bmp = CreateTestBitmap(); // 32x32
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = bmp });

            Assert.Equal("32 × 32", vm.ResolutionText);
            Assert.Equal(PopupPosition.TopRight, vm.Position);
        });
    }

    [Fact]
    public void DestinationItemViewModel_DisplayIcon_SelectsAppropriateEmoji()
    {
        var dest1 = new DestinationFolder { Name = "Screenshots", Path = @"C:\Pictures\Screenshots" };
        var item1 = new DestinationItemViewModel(dest1);
        Assert.Equal("📸", item1.DisplayIcon);

        var dest2 = new DestinationFolder { Name = "Desktop", Path = @"C:\Users\User\Desktop" };
        var item2 = new DestinationItemViewModel(dest2);
        Assert.Equal("🖥️", item2.DisplayIcon);

        var dest3 = new DestinationFolder { Name = "Downloads", Path = @"C:\Users\User\Downloads" };
        var item3 = new DestinationItemViewModel(dest3);
        Assert.Equal("📥", item3.DisplayIcon);

        var dest4 = new DestinationFolder { Name = "Documents", Path = @"C:\Users\User\Documents" };
        var item4 = new DestinationItemViewModel(dest4);
        Assert.Equal("📑", item4.DisplayIcon);
    }

    [Fact]
    public void SessionDefaultPath_WhenSet_StartsCollapsed_AndAllowsExpand()
    {
        StaTestHelper.RunInSta(() =>
        {
            var testFolder = Path.Combine(_tempDir, "SessionDir");
            Directory.CreateDirectory(testFolder);
            _settingsService.SessionDefaultPath = testFolder;

            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.True(vm.HasSessionDefault);
            Assert.True(vm.IsCollapsed);
            Assert.Equal("SessionDir", vm.SessionDefaultFolderName);

            // Expanding uncollapses and pauses timer
            vm.ExpandToFullList();
            Assert.False(vm.IsCollapsed);
            Assert.True(vm.IsTimerPaused);

            // Clearing default removes it
            vm.ClearSessionDefault();
            Assert.False(vm.HasSessionDefault);
            Assert.Null(_settingsService.SessionDefaultPath);
        });
    }

    [Fact]
    public async Task SaveToCustomPathAsync_SavesScreenshot_AndAddsToPresets()
    {
        await StaTestHelper.RunInStaAsync(async () =>
        {
            var customTarget = Path.Combine(_tempDir, "CustomProjectFolder");
            Directory.CreateDirectory(customTarget);

            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            bool closed = false;
            vm.RequestClose += (s, e) => closed = true;

            bool success = await vm.SaveToCustomPathAsync(customTarget, addToPresets: true, setAsSessionDefault: true);

            Assert.True(success);
            Assert.True(closed);
            Assert.Single(Directory.GetFiles(customTarget, "*.png"));
            Assert.Equal(customTarget, _settingsService.SessionDefaultPath);

            var updatedSettings = _settingsService.CurrentSettings;
            Assert.Contains(updatedSettings.Destinations, d => d.Path.Equals(customTarget, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void CapturePopupView_InstantiatesAndRendersVisualTree_InBothLightAndDarkModes_WithoutExceptions()
    {
        StaTestHelper.RunInSta(() =>
        {
            foreach (var mode in new[] { PopupThemeMode.LightGlass, PopupThemeMode.DarkGlass })
            {
                _settingsService.CurrentSettings.PopupThemeMode = mode;
                var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
                vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

                Assert.Equal(mode, vm.ThemeMode);
                Assert.Equal(mode == PopupThemeMode.LightGlass, vm.IsLightGlass);
                Assert.Equal(mode == PopupThemeMode.DarkGlass, vm.IsDarkGlass);

                var popup = new ScreenshotDirector.Views.CapturePopupView
                {
                    DataContext = vm
                };

                popup.Measure(new System.Windows.Size(460, 500));
                popup.Arrange(new System.Windows.Rect(0, 0, 460, 500));
                popup.UpdateLayout();

                Assert.NotNull(popup);
                Assert.True(popup.ActualWidth >= 0);

                popup.Close();
            }
        });
    }

    [Fact]
    public void PointerHover_PausesTimerOnEnter_AndResumesOnLeave()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.False(vm.IsTimerPaused);

            // Mouse moves over window -> timer should pause
            vm.OnPointerEnterWindow();
            Assert.True(vm.IsTimerPaused);

            // Mouse moves away -> timer should resume
            vm.OnPointerLeaveWindow();
            Assert.False(vm.IsTimerPaused);
        });
    }

    [Fact]
    public void PointerHover_WithActiveEditing_StaysPausedUntilEditingEnds()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            // Pointer enters and user begins editing
            vm.OnPointerEnterWindow();
            Assert.True(vm.IsTimerPaused);

            vm.SetEditingFilename(true);
            Assert.True(vm.IsTimerPaused);

            // Mouse moves outside the window while editing is active -> timer remains paused
            vm.OnPointerLeaveWindow();
            Assert.True(vm.IsTimerPaused);

            // Once editing finishes (lost focus) -> timer resumes
            vm.SetEditingFilename(false);
            Assert.False(vm.IsTimerPaused);
        });
    }

    [Fact]
    public void ResetPointerHoverState_ResumesTimerIfPausedByHover()
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
            Assert.False(vm.IsTimerPaused);

            vm.OnPointerEnterWindow();
            Assert.True(vm.IsTimerPaused);

            vm.ResetPointerHoverState();
            Assert.False(vm.IsTimerPaused);
        });
    }

    [Fact]
    public void DetailsExpansion_OpensCollapsed_ExpandsOnPointerEnter_AndCollapsesOnLeave()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            // Popup opens compact: routing details stay hidden until hover.
            Assert.False(vm.IsDetailsExpanded);

            vm.OnPointerEnterWindow();
            Assert.True(vm.IsDetailsExpanded);

            vm.OnPointerLeaveWindow();
            Assert.False(vm.IsDetailsExpanded);
        });
    }

    [Fact]
    public void DetailsExpansion_StaysOpen_WhenCustomPathIsSelected()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.False(vm.IsDetailsExpanded);

            // Selecting a custom path pins the drawer open, even without hover.
            vm.IsCustomPathSelected = true;
            Assert.True(vm.IsDetailsExpanded);

            vm.OnPointerLeaveWindow();
            Assert.True(vm.IsDetailsExpanded);

            // Cancelling the custom path lets the popup collapse again.
            vm.IsCustomPathSelected = false;
            Assert.False(vm.IsDetailsExpanded);
        });
    }

    [Fact]
    public void ExpandToFullList_ExpandsDetails_AndPausesTimer()
    {
        StaTestHelper.RunInSta(() =>
        {
            var testFolder = Path.Combine(_tempDir, "SessionDir");
            Directory.CreateDirectory(testFolder);
            _settingsService.SessionDefaultPath = testFolder;

            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            Assert.True(vm.IsCollapsed);
            Assert.False(vm.IsDetailsExpanded);

            vm.ExpandToFullList();

            Assert.False(vm.IsCollapsed);
            Assert.True(vm.IsDetailsExpanded);
            Assert.True(vm.IsTimerPaused);
        });
    }

    [Fact]
    public void ResetPointerHoverState_CollapsesDetails_UnlessCustomPathIsSelected()
    {
        StaTestHelper.RunInSta(() =>
        {
            var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
            vm.Initialize(new CaptureCandidate { Generation = 1, ImageSource = CreateTestBitmap() });

            vm.OnPointerEnterWindow();
            Assert.True(vm.IsDetailsExpanded);

            // Window deactivation resets hover, so the popup falls back to compact.
            vm.ResetPointerHoverState();
            Assert.False(vm.IsDetailsExpanded);

            vm.IsCustomPathSelected = true;
            vm.OnPointerEnterWindow();
            vm.ResetPointerHoverState();

            Assert.True(vm.IsDetailsExpanded);
        });
    }
}



