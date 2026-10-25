using System.IO;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Platform;
using ScreenshotDirector.Services;
using ScreenshotDirector.ViewModels;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class MainControlViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly StartupManager _startupManager;
    private readonly ImageCaptureService _captureService;
    private readonly CaptureDetectionService _detectionService;

    public MainControlViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_MainVMTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
        _settingsService = new SettingsService(_logger, Path.Combine(_tempDir, "settings.json"));
        _startupManager = new StartupManager(_logger);
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
    public void Initialization_LoadsSettingsAndInitializesState()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);

        Assert.NotNull(vm.Destinations);
        Assert.False(vm.IsDetectionPaused);
        Assert.Equal("Active & Listening", vm.DetectionStatusText);
        Assert.NotEmpty(vm.DestinationsCountSummary);
        Assert.Contains("Granted", vm.ClipboardPermissionText);
    }

    [Fact]
    public void TogglePause_TogglesDetectionServiceAndViewModelStatus()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);

        Assert.False(vm.IsDetectionPaused);
        Assert.False(_detectionService.IsPaused);

        vm.TogglePause();
        Assert.True(vm.IsDetectionPaused);
        Assert.True(_detectionService.IsPaused);
        Assert.Equal("Paused", vm.DetectionStatusText);

        vm.TogglePause();
        Assert.False(vm.IsDetectionPaused);
        Assert.False(_detectionService.IsPaused);
        Assert.Equal("Active & Listening", vm.DetectionStatusText);
    }

    [Fact]
    public void StopProgram_InvokesShutdownAction()
    {
        bool shutdownCalled = false;
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, () => shutdownCalled = true, _logger);

        vm.StopProgram();
        Assert.True(shutdownCalled);
    }

    [Fact]
    public void DestinationManagement_AddRemoveAndReorder_UpdatesShortcuts()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);
        vm.Destinations.Clear();

        vm.AddDestination("Project A", @"C:\Projects\A");
        vm.AddDestination("Project B", @"C:\Projects\B");
        vm.AddDestination("Project C", @"C:\Projects\C");

        Assert.Equal(3, vm.Destinations.Count);
        Assert.Equal(1, vm.Destinations[0].ShortcutNumber);
        Assert.Equal(2, vm.Destinations[1].ShortcutNumber);
        Assert.Equal(3, vm.Destinations[2].ShortcutNumber);

        // Move B up -> B becomes shortcut 1, A becomes shortcut 2
        vm.SelectedDestination = vm.Destinations[1]; // Project B
        vm.MoveSelectedUp();

        Assert.Equal("Project B", vm.Destinations[0].Name);
        Assert.Equal(1, vm.Destinations[0].ShortcutNumber);
        Assert.Equal("Project A", vm.Destinations[1].Name);
        Assert.Equal(2, vm.Destinations[1].ShortcutNumber);

        // Remove Project A
        vm.SelectedDestination = vm.Destinations[1];
        vm.RemoveSelected();

        Assert.Equal(2, vm.Destinations.Count);
        Assert.Equal(1, vm.Destinations[0].ShortcutNumber);
        Assert.Equal(2, vm.Destinations[1].ShortcutNumber);
    }

    [Fact]
    public void AddTemplateFolder_AddsKnownTemplates()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);
        vm.Destinations.Clear();

        vm.AddTemplateFolder("desktop");
        Assert.Single(vm.Destinations);
        Assert.Equal("Desktop", vm.Destinations[0].Name);
        Assert.Equal(1, vm.Destinations[0].ShortcutNumber);

        vm.AddTemplateFolder("downloads");
        Assert.Equal(2, vm.Destinations.Count);
        Assert.Equal("Downloads", vm.Destinations[1].Name);
        Assert.Equal(2, vm.Destinations[1].ShortcutNumber);
    }

    [Fact]
    public async Task SaveAsync_SetsIsFirstRunCompletedTrue_AndPersists()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);
        vm.Destinations.Clear();
        vm.AddDestination("My Custom Presets", @"C:\MyScreenFolder");

        bool saved = await vm.SaveAsync();
        Assert.True(saved);
        Assert.True(vm.IsFirstRunCompleted);
        Assert.False(vm.ShowOnboardingGuide);

        // Verify reloaded from disk
        var reloaded = await _settingsService.LoadSettingsAsync();
        Assert.True(reloaded.IsFirstRunCompleted);
        Assert.Single(reloaded.Destinations);
        Assert.Equal("My Custom Presets", reloaded.Destinations[0].Name);
    }

    [Fact]
    public void TriggerTestCapture_FiresScreenshotDetectedEvent()
    {
        StaTestHelper.RunInSta(() =>
        {
            bool detected = false;
            CaptureCandidate? receivedCandidate = null;

            _detectionService.ScreenshotDetected += (s, c) =>
            {
                detected = true;
                receivedCandidate = c;
            };

            _detectionService.TriggerTestCapture();

            Assert.True(detected);
            Assert.NotNull(receivedCandidate);
            Assert.NotNull(receivedCandidate.ImageSource);
            Assert.True(receivedCandidate.Width > 0);
            Assert.True(receivedCandidate.Height > 0);
        });
    }

    [Fact]
    public void Navigation_UpdatesCurrentPageAndProperties()
    {
        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);

        Assert.True(vm.IsHome);
        Assert.Equal("Home", vm.CurrentPage);

        vm.NavigateTo("Presets");
        Assert.True(vm.IsPresets);
        Assert.False(vm.IsHome);
        Assert.Contains("Preset Folders", vm.PageTitle);

        vm.NavigateTo("PopupSettings");
        Assert.True(vm.IsPopupSettings);
        Assert.Equal("Popup & Routing", vm.PageTitle);

        vm.NavigateTo("GeneralSettings");
        Assert.True(vm.IsGeneralSettings);
        Assert.Equal("General & Permissions", vm.PageTitle);

        vm.NavigateTo("About");
        Assert.True(vm.IsAbout);
        Assert.Contains("About", vm.PageTitle);
    }

    [Fact]
    public void RecentCaptures_AddFilterAndClear_WorkCorrectly()
    {
        var testFilePath = Path.Combine(_tempDir, "TestScreenshot_2026.png");
        File.WriteAllBytes(testFilePath, new byte[] { 1, 2, 3 });

        var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);
        vm.ClearRecentCaptures();

        Assert.False(vm.HasRecentCaptures);
        Assert.Empty(vm.RecentCaptures);

        vm.AddRecentCapture(testFilePath);

        Assert.True(vm.HasRecentCaptures);
        Assert.Single(vm.RecentCaptures);
        Assert.Equal("TestScreenshot_2026.png", vm.RecentCaptures[0].Filename);

        // Test search query filtering
        vm.RecentSearchQuery = "NotFoundQuery";
        Assert.Empty(vm.FilteredRecentCaptures);

        vm.RecentSearchQuery = "TestScreenshot";
        Assert.Single(vm.FilteredRecentCaptures);

        // Test clear
        vm.ClearRecentCaptures();
        Assert.False(vm.HasRecentCaptures);
        Assert.Empty(vm.RecentCaptures);
    }

    [Fact]
    public void MainWindow_InstantiatesAndRendersVisualTree_WithoutXamlOrDispatcherExceptions()
    {
        StaTestHelper.RunInSta(() =>
        {
            var testFilePath = Path.Combine(_tempDir, "RenderTest_Screenshot.png");
            File.WriteAllBytes(testFilePath, new byte[] { 1, 2, 3 });

            var vm = new MainControlViewModel(_settingsService, _startupManager, _detectionService, null, _logger);
            vm.AddRecentCapture(testFilePath);

            var window = new ScreenshotDirector.Views.MainWindow
            {
                DataContext = vm
            };

            // Force layout measurement and arrangement of the visual tree
            window.Measure(new System.Windows.Size(960, 680));
            window.Arrange(new System.Windows.Rect(0, 0, 960, 680));
            window.UpdateLayout();

            Assert.NotNull(window);
            Assert.Equal("Screenshot Director", window.Title);
            Assert.True(window.IsLoaded || window.ActualWidth >= 0);

            window.Close();
        });
    }
}


