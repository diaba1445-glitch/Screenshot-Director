namespace ScreenshotDirector.Core.Models;

public class DestinationFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int ShortcutNumber { get; set; }
    public bool IsEnabled { get; set; } = true;

    public bool Exists => !string.IsNullOrWhiteSpace(Path) && System.IO.Directory.Exists(Path);

    public DestinationFolder Clone() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path,
        ShortcutNumber = ShortcutNumber,
        IsEnabled = IsEnabled
    };
}
