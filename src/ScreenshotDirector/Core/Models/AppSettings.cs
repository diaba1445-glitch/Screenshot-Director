using System.Text.Json.Serialization;

namespace ScreenshotDirector.Core.Models;

public enum CollisionPolicy
{
    NumberedSuffix,
    Reject
}

public enum ClipboardDetectionMode
{
    Conservative,
    GenericFallback
}

public class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = true;

    [JsonPropertyName("runInSystemTray")]
    public bool RunInSystemTray { get; set; } = true;

    [JsonPropertyName("popupTimeoutSeconds")]
    public int PopupTimeoutSeconds { get; set; } = 7;

    [JsonPropertyName("defaultFormat")]
    public string DefaultFormat { get; set; } = "PNG";

    [JsonPropertyName("copySavedPathToClipboard")]
    public bool CopySavedPathToClipboard { get; set; } = true;

    [JsonPropertyName("allowQuickRename")]
    public bool AllowQuickRename { get; set; } = true;

    [JsonPropertyName("defaultFilenamePattern")]
    public string DefaultFilenamePattern { get; set; } = "Screenshot_yyyy-MM-dd_HH-mm-ss.png";

    [JsonPropertyName("collisionPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionPolicy CollisionPolicy { get; set; } = CollisionPolicy.NumberedSuffix;

    [JsonPropertyName("clipboardDetectionMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClipboardDetectionMode ClipboardDetectionMode { get; set; } = ClipboardDetectionMode.Conservative;

    [JsonPropertyName("genericImageFallbackEnabled")]
    public bool GenericImageFallbackEnabled { get; set; } = false;

    [JsonPropertyName("destinations")]
    public List<DestinationFolder> Destinations { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        // Realistic user defaults based on well-known standard folders
        var picturesScreenshots = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var downloads = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        int shortcut = 1;
        void AddDest(string name, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                settings.Destinations.Add(new DestinationFolder
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Path = path,
                    ShortcutNumber = shortcut++,
                    IsEnabled = true
                });
            }
        }

        AddDest("Screenshots", picturesScreenshots);
        AddDest("Desktop", desktop);
        AddDest("Downloads", downloads);
        AddDest("Documents", documents);

        return settings;
    }
}
