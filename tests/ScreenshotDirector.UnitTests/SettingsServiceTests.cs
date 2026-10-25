using System.IO;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Services;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFile;
    private readonly AppLogger _logger;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_SettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
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
    public async Task LoadSettingsAsync_CreatesDefaultsIfFileMissing()
    {
        var service = new SettingsService(_logger, _settingsFile);
        var settings = await service.LoadSettingsAsync();

        Assert.NotNull(settings);
        Assert.True(File.Exists(_settingsFile));
        Assert.Equal(1, settings.SchemaVersion);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.RunInSystemTray);
        Assert.Equal(7, settings.PopupTimeoutSeconds);
        Assert.NotEmpty(settings.Destinations);
        Assert.Equal(1, settings.Destinations[0].ShortcutNumber);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsAndSurvivesReload()
    {
        var service = new SettingsService(_logger, _settingsFile);
        var settings = await service.LoadSettingsAsync();

        settings.PopupTimeoutSeconds = 12;
        settings.Destinations.Add(new DestinationFolder
        {
            Name = "Custom Project",
            Path = @"C:\Projects\Screen",
            ShortcutNumber = 5
        });

        await service.SaveSettingsAsync(settings);

        // Reload with fresh service instance
        var reloadedService = new SettingsService(_logger, _settingsFile);
        var reloadedSettings = await reloadedService.LoadSettingsAsync();

        Assert.Equal(12, reloadedSettings.PopupTimeoutSeconds);
        Assert.Contains(reloadedSettings.Destinations, d => d.Name == "Custom Project");
    }

    [Fact]
    public async Task LoadSettingsAsync_RecoversFromCorruptJsonAndBacksUp()
    {
        await File.WriteAllTextAsync(_settingsFile, "{ invalid_json_corrupted !!");

        var service = new SettingsService(_logger, _settingsFile);
        var settings = await service.LoadSettingsAsync();

        Assert.NotNull(settings);
        Assert.Equal(7, settings.PopupTimeoutSeconds); // restored to defaults
        Assert.True(File.Exists(_settingsFile + ".corrupt.bak")); // backup created
    }

    [Fact]
    public async Task LoadSettingsAsync_DeduplicatesDestinationIdsAndAssignsShortcuts()
    {
        var service = new SettingsService(_logger, _settingsFile);
        var settings = await service.LoadSettingsAsync();

        // Add items with identical IDs and bad shortcuts
        settings.Destinations.Clear();
        settings.Destinations.Add(new DestinationFolder { Id = "DUPE", Name = "Folder 1", Path = @"C:\1", ShortcutNumber = 0 });
        settings.Destinations.Add(new DestinationFolder { Id = "DUPE", Name = "Folder 2", Path = @"C:\2", ShortcutNumber = 0 });
        settings.Destinations.Add(new DestinationFolder { Id = "DUPE", Name = "Folder 3", Path = @"C:\3", ShortcutNumber = 0 });

        await service.SaveSettingsAsync(settings);

        var reloaded = await service.LoadSettingsAsync();
        var ids = reloaded.Destinations.Select(d => d.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count); // all unique now
        Assert.Equal(1, reloaded.Destinations[0].ShortcutNumber);
        Assert.Equal(2, reloaded.Destinations[1].ShortcutNumber);
        Assert.Equal(3, reloaded.Destinations[2].ShortcutNumber);
    }
}
