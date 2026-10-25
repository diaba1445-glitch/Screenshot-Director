using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.ViewModels;

public class DestinationEditItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private int _shortcutNumber;
    private bool _isEnabled = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(); }
    }

    public int ShortcutNumber
    {
        get => _shortcutNumber;
        set { _shortcutNumber = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public DestinationEditItem(DestinationFolder folder)
    {
        Id = folder.Id;
        Name = folder.Name;
        Path = folder.Path;
        ShortcutNumber = folder.ShortcutNumber;
        IsEnabled = folder.IsEnabled;
    }

    public DestinationFolder ToModel() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path,
        ShortcutNumber = ShortcutNumber,
        IsEnabled = IsEnabled
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupManager _startupManager;
    private readonly IAppLogger _logger;

    private bool _startWithWindows;
    private bool _runInSystemTray;
    private int _popupTimeoutSeconds;
    private bool _copySavedPathToClipboard;
    private bool _allowQuickRename;
    private string _defaultFilenamePattern = "Screenshot_yyyy-MM-dd_HH-mm-ss.png";
    private bool _genericImageFallbackEnabled;
    private DestinationEditItem? _selectedDestination;
    private string _statusMessage = string.Empty;

    public event EventHandler? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DestinationEditItem> Destinations { get; } = new();

    public DestinationEditItem? SelectedDestination
    {
        get => _selectedDestination;
        set { _selectedDestination = value; OnPropertyChanged(); }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set { _startWithWindows = value; OnPropertyChanged(); }
    }

    public bool RunInSystemTray
    {
        get => _runInSystemTray;
        set { _runInSystemTray = value; OnPropertyChanged(); }
    }

    public int PopupTimeoutSeconds
    {
        get => _popupTimeoutSeconds;
        set { _popupTimeoutSeconds = value; OnPropertyChanged(); }
    }

    public bool CopySavedPathToClipboard
    {
        get => _copySavedPathToClipboard;
        set { _copySavedPathToClipboard = value; OnPropertyChanged(); }
    }

    public bool AllowQuickRename
    {
        get => _allowQuickRename;
        set { _allowQuickRename = value; OnPropertyChanged(); }
    }

    public string DefaultFilenamePattern
    {
        get => _defaultFilenamePattern;
        set { _defaultFilenamePattern = value; OnPropertyChanged(); }
    }

    public bool GenericImageFallbackEnabled
    {
        get => _genericImageFallbackEnabled;
        set { _genericImageFallbackEnabled = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupManager startupManager,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _startupManager = startupManager;
        _logger = logger;

        LoadFromSettings(_settingsService.CurrentSettings);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        StartWithWindows = settings.StartWithWindows;
        RunInSystemTray = settings.RunInSystemTray;
        PopupTimeoutSeconds = settings.PopupTimeoutSeconds;
        CopySavedPathToClipboard = settings.CopySavedPathToClipboard;
        AllowQuickRename = settings.AllowQuickRename;
        DefaultFilenamePattern = settings.DefaultFilenamePattern;
        GenericImageFallbackEnabled = settings.GenericImageFallbackEnabled;

        Destinations.Clear();
        foreach (var dest in settings.Destinations.OrderBy(d => d.ShortcutNumber))
        {
            Destinations.Add(new DestinationEditItem(dest));
        }

        UpdateShortcutNumbers();
    }

    public void AddDestination(string name, string path)
    {
        var item = new DestinationEditItem(new DestinationFolder
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Path = path,
            IsEnabled = true
        });
        Destinations.Add(item);
        UpdateShortcutNumbers();
        SelectedDestination = item;
    }

    public void RemoveSelected()
    {
        if (SelectedDestination != null)
        {
            Destinations.Remove(SelectedDestination);
            UpdateShortcutNumbers();
            SelectedDestination = Destinations.FirstOrDefault();
        }
    }

    public void MoveSelectedUp()
    {
        if (SelectedDestination == null) return;
        int index = Destinations.IndexOf(SelectedDestination);
        if (index > 0)
        {
            Destinations.Move(index, index - 1);
            UpdateShortcutNumbers();
        }
    }

    public void MoveSelectedDown()
    {
        if (SelectedDestination == null) return;
        int index = Destinations.IndexOf(SelectedDestination);
        if (index >= 0 && index < Destinations.Count - 1)
        {
            Destinations.Move(index, index + 1);
            UpdateShortcutNumbers();
        }
    }

    public void BrowseSelectedPath()
    {
        if (SelectedDestination == null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Destination Folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(SelectedDestination.Path) && System.IO.Directory.Exists(SelectedDestination.Path))
        {
            dialog.SelectedPath = SelectedDestination.Path;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SelectedDestination.Path = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(SelectedDestination.Name))
            {
                SelectedDestination.Name = System.IO.Path.GetFileName(dialog.SelectedPath);
            }
        }
    }

    private void UpdateShortcutNumbers()
    {
        int num = 1;
        foreach (var item in Destinations)
        {
            item.ShortcutNumber = num <= 9 ? num++ : 0;
        }
    }

    public async Task<bool> SaveAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                StartWithWindows = StartWithWindows,
                RunInSystemTray = RunInSystemTray,
                PopupTimeoutSeconds = PopupTimeoutSeconds,
                CopySavedPathToClipboard = CopySavedPathToClipboard,
                AllowQuickRename = AllowQuickRename,
                DefaultFilenamePattern = DefaultFilenamePattern,
                GenericImageFallbackEnabled = GenericImageFallbackEnabled,
                ClipboardDetectionMode = GenericImageFallbackEnabled 
                    ? ClipboardDetectionMode.GenericFallback 
                    : ClipboardDetectionMode.Conservative,
                Destinations = Destinations.Select(d => d.ToModel()).ToList()
            };

            await _settingsService.SaveSettingsAsync(settings);

            // Apply startup configuration
            _startupManager.SetStartupEnabled(StartWithWindows);

            StatusMessage = "Settings saved successfully.";
            RequestClose?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings from view model", ex);
            StatusMessage = $"Save error: {ex.Message}";
            return false;
        }
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
