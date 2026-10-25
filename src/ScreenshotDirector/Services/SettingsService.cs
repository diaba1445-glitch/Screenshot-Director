using System.IO;
using System.Text.Json;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;
    private readonly IAppLogger _logger;
    private readonly object _lock = new();

    private AppSettings _currentSettings;

    public AppSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
        private set
        {
            lock (_lock)
            {
                _currentSettings = value;
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(IAppLogger logger, string? customSettingsPath = null)
    {
        _logger = logger;

        if (!string.IsNullOrEmpty(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
            _settingsDirectory = Path.GetDirectoryName(customSettingsPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScreenshotDirector");
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }

        _currentSettings = AppSettings.CreateDefault();
    }

    public string GetSettingsFilePath() => _settingsFilePath;

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.Info("Settings file does not exist. Creating default settings.");
                var defaults = AppSettings.CreateDefault();
                await SaveSettingsAsync(defaults);
                CurrentSettings = defaults;
                return defaults;
            }

            string json;
            using (var reader = new StreamReader(_settingsFilePath))
            {
                json = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.Warning("Settings file was empty. Restoring defaults.");
                var defaults = AppSettings.CreateDefault();
                await SaveSettingsAsync(defaults);
                CurrentSettings = defaults;
                return defaults;
            }

            AppSettings? loaded = null;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                loaded = JsonSerializer.Deserialize<AppSettings>(json, options);
            }
            catch (JsonException jsonEx)
            {
                _logger.Error("Corrupt settings JSON detected. Creating backup and resetting to defaults.", jsonEx);
                BackupCorruptSettings();
                var defaults = AppSettings.CreateDefault();
                await SaveSettingsAsync(defaults);
                CurrentSettings = defaults;
                return defaults;
            }

            if (loaded == null)
            {
                loaded = AppSettings.CreateDefault();
            }

            // Schema Migration
            if (loaded.SchemaVersion < 1)
            {
                _logger.Info($"Migrating schema from {loaded.SchemaVersion} to 1");
                loaded.SchemaVersion = 1;
                await SaveSettingsAsync(loaded);
            }

            // Validate and sanitize
            ValidateAndSanitize(loaded);

            CurrentSettings = loaded;
            _logger.Info($"Settings loaded successfully. {loaded.Destinations.Count} destination(s) registered.");
            return loaded;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load settings file. Falling back to defaults in memory.", ex);
            var defaults = AppSettings.CreateDefault();
            CurrentSettings = defaults;
            return defaults;
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ValidateAndSanitize(settings);

        try
        {
            Directory.CreateDirectory(_settingsDirectory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(settings, options);

            string tempFile = Path.Combine(_settingsDirectory, $"settings_{Guid.NewGuid():N}.tmp");

            // Atomic file write: write to temp file then replace
            using (var writer = new StreamWriter(tempFile, false))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync();
            }

            lock (_lock)
            {
                if (File.Exists(_settingsFilePath))
                {
                    File.Replace(tempFile, _settingsFilePath, null, true);
                }
                else
                {
                    File.Move(tempFile, _settingsFilePath);
                }

                _currentSettings = settings;
            }

            _logger.Info("Settings saved successfully.");
            SettingsChanged?.Invoke(this, settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings", ex);
            throw;
        }
    }

    private void ValidateAndSanitize(AppSettings settings)
    {
        if (settings.PopupTimeoutSeconds < 1 || settings.PopupTimeoutSeconds > 60)
        {
            settings.PopupTimeoutSeconds = 7;
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultFilenamePattern))
        {
            settings.DefaultFilenamePattern = "Screenshot_yyyy-MM-dd_HH-mm-ss.png";
        }

        // Deduplicate destination IDs
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int shortcutCounter = 1;

        foreach (var dest in settings.Destinations)
        {
            if (string.IsNullOrWhiteSpace(dest.Id) || !seenIds.Add(dest.Id))
            {
                dest.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(dest.Id);
            }

            if (string.IsNullOrWhiteSpace(dest.Name))
            {
                dest.Name = !string.IsNullOrWhiteSpace(dest.Path)
                    ? Path.GetFileName(dest.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : "Folder";
            }

            if (shortcutCounter <= 9)
            {
                dest.ShortcutNumber = shortcutCounter++;
            }
            else
            {
                dest.ShortcutNumber = 0;
            }
        }
    }

    private void BackupCorruptSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string backupPath = _settingsFilePath + ".corrupt.bak";
                File.Copy(_settingsFilePath, backupPath, true);
                _logger.Info($"Saved backup of corrupt settings to {backupPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to backup corrupt settings file", ex);
        }
    }
}
