using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Core.Interfaces;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    event EventHandler<AppSettings>? SettingsChanged;

    string? SessionDefaultPath { get; set; }
    event EventHandler<string?>? SessionDefaultPathChanged;

    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    string GetSettingsFilePath();
}
