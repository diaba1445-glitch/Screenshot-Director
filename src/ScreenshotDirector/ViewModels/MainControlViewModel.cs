using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.ViewModels;

public class RecentCaptureItem
{
    public string Filename { get; set; } = string.Empty;
    public string FileName { get => Filename; set => Filename = value; }
    public string FilePath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string DirectoryPath { get => FolderPath; set => FolderPath = value; }
    public string TimeFormatted { get; set; } = string.Empty;
    public string DisplayTime { get => TimeFormatted; set => TimeFormatted = value; }
    public string SizeFormatted { get; set; } = string.Empty;
    public string DisplaySize { get => SizeFormatted; set => SizeFormatted = value; }
    public string DetailsText => $"{TimeFormatted} · {SizeFormatted}";
    public bool Exists => File.Exists(FilePath);
}

public class MainControlViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupManager _startupManager;
    private readonly ICaptureDetectionService? _detectionService;
    private readonly IImageSaveService? _saveService;
    private readonly Action? _shutdownAction;
    private readonly IAppLogger _logger;

    private bool _isFirstRunCompleted;
    private bool _startWithWindows;
    private bool _runInSystemTray;
    private int _popupTimeoutSeconds;
    private bool _copySavedPathToClipboard;
    private bool _allowQuickRename;
    private string _defaultFilenamePattern = "Screenshot_yyyy-MM-dd_HH-mm-ss.png";
    private bool _genericImageFallbackEnabled;
    private PopupPosition _popupPosition = PopupPosition.TopRight;
    private PopupThemeMode _popupThemeMode = PopupThemeMode.LightGlass;
    private DestinationEditItem? _selectedDestination;
    private string _statusMessage = string.Empty;
    private bool _isDetectionPaused;

    public event EventHandler? RequestClose;
    public event EventHandler? RequestHideToTray;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DestinationEditItem> Destinations { get; } = new();

    public DestinationEditItem? SelectedDestination
    {
        get => _selectedDestination;
        set { _selectedDestination = value; OnPropertyChanged(); }
    }

    public bool IsFirstRunCompleted
    {
        get => _isFirstRunCompleted;
        set
        {
            _isFirstRunCompleted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowOnboardingGuide));
        }
    }

    public bool ShowOnboardingGuide => !_isFirstRunCompleted || Destinations.Count == 0;

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

    public PopupPosition PopupPosition
    {
        get => _popupPosition;
        set { _popupPosition = value; OnPropertyChanged(); }
    }

    public PopupPosition[] AvailablePopupPositions => new[]
    {
        PopupPosition.TopRight,
        PopupPosition.BottomRight,
        PopupPosition.BottomCenter,
        PopupPosition.CursorAdjacent
    };

    public PopupThemeMode PopupThemeMode
    {
        get => _popupThemeMode;
        set
        {
            if (_popupThemeMode != value)
            {
                _popupThemeMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPopupThemeLight));
                OnPropertyChanged(nameof(IsPopupThemeDark));
            }
        }
    }

    public bool IsPopupThemeLight => PopupThemeMode == PopupThemeMode.LightGlass;
    public bool IsPopupThemeDark => PopupThemeMode == PopupThemeMode.DarkGlass;

    public void SetPopupTheme(PopupThemeMode mode)
    {
        PopupThemeMode = mode;
        StatusMessage = mode == PopupThemeMode.LightGlass
            ? "Popup theme set to Light Glass (frosted crystal)."
            : "Popup theme set to Dark Glass (smoked acrylic).";
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

    public bool IsDetectionPaused
    {
        get => _isDetectionPaused;
        private set
        {
            if (_isDetectionPaused != value)
            {
                _isDetectionPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetectionStatusText));
                OnPropertyChanged(nameof(DetectionStatusBadgeColor));
                OnPropertyChanged(nameof(PauseResumeButtonText));
            }
        }
    }

    public string DetectionStatusText => IsDetectionPaused ? "Paused" : "Active & Listening";
    public string DetectionStatusBadgeColor => IsDetectionPaused ? "#F59E0B" : "#10B981"; // Amber or Emerald
    public string PauseResumeButtonText => IsDetectionPaused ? "▶ Resume Detection" : "⏸ Pause Detection";

    public string DestinationsCountSummary => $"{Destinations.Count} of 9 shortcut keys assigned (1–{Math.Min(Destinations.Count, 9)})";

    public string ClipboardPermissionText => "Granted (Native Win32 clipboard format listener active)";
    public string StartupPermissionText => _startupManager.IsStartupEnabled()
        ? "Enabled (HKCU Run key registered)"
        : "Disabled";

    public bool HasSessionDefault => !string.IsNullOrEmpty(_settingsService.SessionDefaultPath);
    public string SessionDefaultDisplayPath => !string.IsNullOrEmpty(_settingsService.SessionDefaultPath)
        ? _settingsService.SessionDefaultPath
        : "None (Popup asks destination each time)";

    private string _currentPage = "Home";
    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(IsPresets));
                OnPropertyChanged(nameof(IsPopupSettings));
                OnPropertyChanged(nameof(IsGeneralSettings));
                OnPropertyChanged(nameof(IsAbout));
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageSubtitle));
            }
        }
    }

    public bool IsHome => CurrentPage == "Home";
    public bool IsPresets => CurrentPage == "Presets";
    public bool IsPopupSettings => CurrentPage == "PopupSettings";
    public bool IsGeneralSettings => CurrentPage == "GeneralSettings";
    public bool IsAbout => CurrentPage == "About";

    public string PageTitle => CurrentPage switch
    {
        "Home" => "Home",
        "Presets" => "Preset Folders (Keys 1–9)",
        "PopupSettings" => "Popup & Routing",
        "GeneralSettings" => "General & Permissions",
        "About" => "About Screenshot Director",
        _ => "Home"
    };

    public string PageSubtitle => CurrentPage switch
    {
        "Home" => "Live screenshot listening, quick test & recent routing activity",
        "Presets" => "Map screenshot destination folders to keys 1–9",
        "PopupSettings" => "Configure screen position, animation, and session default routing",
        "GeneralSettings" => "Windows startup, tray minimization, and system permissions",
        "About" => "Lightweight, instant screenshot router for Windows",
        _ => string.Empty
    };

    public void NavigateTo(string pageName) => CurrentPage = pageName;

    public ObservableCollection<RecentCaptureItem> RecentCaptures { get; } = new();
    public bool HasRecentCaptures => RecentCaptures.Count > 0;

    private string _recentSearchQuery = string.Empty;
    public string RecentSearchQuery
    {
        get => _recentSearchQuery;
        set
        {
            if (_recentSearchQuery != value)
            {
                _recentSearchQuery = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredRecentCaptures));
            }
        }
    }

    public IEnumerable<RecentCaptureItem> FilteredRecentCaptures
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RecentSearchQuery))
                return RecentCaptures;
            return RecentCaptures.Where(c => 
                c.Filename.Contains(RecentSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                c.FolderPath.Contains(RecentSearchQuery, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddRecentCapture(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            long bytes = 0;
            if (File.Exists(filePath))
            {
                bytes = new FileInfo(filePath).Length;
            }
            string sizeStr = bytes > 1024 * 1024
                ? $"{(bytes / 1024.0 / 1024.0):F1} MB"
                : $"{(bytes / 1024.0):F0} KB";

            var item = new RecentCaptureItem
            {
                Filename = Path.GetFileName(filePath),
                FilePath = filePath,
                FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                TimeFormatted = DateTime.Now.ToString("g"),
                SizeFormatted = sizeStr
            };

            void InsertItem()
            {
                RecentCaptures.Insert(0, item);
                while (RecentCaptures.Count > 25)
                {
                    RecentCaptures.RemoveAt(RecentCaptures.Count - 1);
                }
                OnPropertyChanged(nameof(HasRecentCaptures));
                OnPropertyChanged(nameof(FilteredRecentCaptures));
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(InsertItem);
            }
            else
            {
                InsertItem();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to add recent capture to list", ex);
        }
    }

    public void ClearRecentCaptures()
    {
        RecentCaptures.Clear();
        OnPropertyChanged(nameof(HasRecentCaptures));
        OnPropertyChanged(nameof(FilteredRecentCaptures));
    }

    public void OpenFile(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open file: {filePath}", ex);
            }
        }
    }

    public void OpenFolder(string? folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open folder: {folderPath}", ex);
            }
        }
    }

    private void SeedRecentCapturesFromDisk()
    {
        try
        {
            var candidateDirs = Destinations
                .Select(d => d.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct();

            var recentFiles = new List<string>();
            foreach (var dir in candidateDirs)
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*.png")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTime)
                        .Take(5)
                        .Select(fi => fi.FullName);
                    recentFiles.AddRange(files);
                }
                catch { }
            }

            foreach (var file in recentFiles.OrderByDescending(File.GetLastWriteTime).Take(10))
            {
                AddRecentCapture(file);
            }
        }
        catch { }
    }

    public MainControlViewModel(
        ISettingsService settingsService,
        IStartupManager startupManager,
        ICaptureDetectionService? detectionService,
        IImageSaveService? saveService,
        Action? shutdownAction,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _startupManager = startupManager;
        _detectionService = detectionService;
        _saveService = saveService;
        _shutdownAction = shutdownAction;
        _logger = logger;

        if (_saveService != null)
        {
            _saveService.ImageSaved += (s, result) =>
            {
                if (result.Success && !string.IsNullOrWhiteSpace(result.SavedFilePath))
                {
                    AddRecentCapture(result.SavedFilePath);
                }
            };
        }

        _settingsService.SessionDefaultPathChanged += (s, path) =>
        {
            OnPropertyChanged(nameof(HasSessionDefault));
            OnPropertyChanged(nameof(SessionDefaultDisplayPath));
        };

        _isDetectionPaused = _detectionService?.IsPaused ?? false;
        LoadFromSettings(_settingsService.CurrentSettings);
        SeedRecentCapturesFromDisk();
    }

    public MainControlViewModel(
        ISettingsService settingsService,
        IStartupManager startupManager,
        ICaptureDetectionService? detectionService,
        Action? shutdownAction,
        IAppLogger logger)
        : this(settingsService, startupManager, detectionService, null, shutdownAction, logger)
    {
    }

    public void SetSessionDefault(string path)
    {
        _settingsService.SessionDefaultPath = path;
        OnPropertyChanged(nameof(HasSessionDefault));
        OnPropertyChanged(nameof(SessionDefaultDisplayPath));
        StatusMessage = $"Session default folder set to: {Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
    }

    public void ClearSessionDefault()
    {
        _settingsService.SessionDefaultPath = null;
        OnPropertyChanged(nameof(HasSessionDefault));
        OnPropertyChanged(nameof(SessionDefaultDisplayPath));
        StatusMessage = "Session default folder cleared.";
    }

    public void LoadFromSettings(AppSettings settings)
    {
        IsFirstRunCompleted = settings.IsFirstRunCompleted;
        StartWithWindows = settings.StartWithWindows;
        RunInSystemTray = settings.RunInSystemTray;
        PopupTimeoutSeconds = settings.PopupTimeoutSeconds;
        CopySavedPathToClipboard = settings.CopySavedPathToClipboard;
        AllowQuickRename = settings.AllowQuickRename;
        DefaultFilenamePattern = settings.DefaultFilenamePattern;
        GenericImageFallbackEnabled = settings.GenericImageFallbackEnabled;
        PopupPosition = settings.PopupPosition;
        PopupThemeMode = settings.PopupThemeMode;

        Destinations.Clear();
        foreach (var dest in settings.Destinations.OrderBy(d => d.ShortcutNumber))
        {
            Destinations.Add(new DestinationEditItem(dest));
        }

        UpdateShortcutNumbers();
        SelectedDestination = Destinations.FirstOrDefault();
        OnPropertyChanged(nameof(ShowOnboardingGuide));
        OnPropertyChanged(nameof(DestinationsCountSummary));
    }

    public void TogglePause()
    {
        if (_detectionService == null) return;

        if (_detectionService.IsPaused)
        {
            _detectionService.Resume();
            IsDetectionPaused = false;
            StatusMessage = "Screenshot detection resumed.";
        }
        else
        {
            _detectionService.Pause();
            IsDetectionPaused = true;
            StatusMessage = "Screenshot detection paused.";
        }
    }

    public void StopProgram()
    {
        _logger.Info("User requested Stop Program from Control Center.");
        RequestClose?.Invoke(this, EventArgs.Empty);
        _shutdownAction?.Invoke();
    }

    public void TestPopup()
    {
        if (_detectionService == null)
        {
            StatusMessage = "Detection service is not available.";
            return;
        }

        StatusMessage = "Triggered test screenshot popup near cursor!";
        _detectionService.TriggerTestCapture();
    }

    public void AddDestination(string name, string path)
    {
        if (Destinations.Count >= 9)
        {
            StatusMessage = "Maximum 9 shortcut presets (keys 1–9) supported.";
            return;
        }

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
        StatusMessage = $"Added destination: {name}";
        OnPropertyChanged(nameof(ShowOnboardingGuide));
        OnPropertyChanged(nameof(DestinationsCountSummary));
    }

    public void AddTemplateFolder(string templateType)
    {
        string name;
        string path;

        switch (templateType.ToLowerInvariant())
        {
            case "screenshots":
                name = "Screenshots";
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                break;
            case "desktop":
                name = "Desktop";
                path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                break;
            case "downloads":
                name = "Downloads";
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                break;
            case "documents":
                name = "Documents";
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                break;
            default:
                BrowseNewDestination();
                return;
        }

        // Avoid adding exact duplicate path if already present
        if (Destinations.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"'{name}' is already in your presets.";
            return;
        }

        AddDestination(name, path);
    }

    public void BrowseNewDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder for your screenshot presets",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            string folderPath = dialog.SelectedPath;
            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Folder";

            AddDestination(folderName, folderPath);
        }
    }

    public void RemoveSelected()
    {
        if (SelectedDestination != null)
        {
            string removedName = SelectedDestination.Name;
            Destinations.Remove(SelectedDestination);
            UpdateShortcutNumbers();
            SelectedDestination = Destinations.FirstOrDefault();
            StatusMessage = $"Removed: {removedName}";
            OnPropertyChanged(nameof(ShowOnboardingGuide));
            OnPropertyChanged(nameof(DestinationsCountSummary));
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
            OnPropertyChanged(nameof(DestinationsCountSummary));
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
            OnPropertyChanged(nameof(DestinationsCountSummary));
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

        if (!string.IsNullOrWhiteSpace(SelectedDestination.Path) && Directory.Exists(SelectedDestination.Path))
        {
            dialog.SelectedPath = SelectedDestination.Path;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SelectedDestination.Path = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(SelectedDestination.Name))
            {
                SelectedDestination.Name = Path.GetFileName(dialog.SelectedPath);
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
            IsFirstRunCompleted = true;

            var settings = new AppSettings
            {
                IsFirstRunCompleted = true,
                StartWithWindows = StartWithWindows,
                RunInSystemTray = RunInSystemTray,
                PopupTimeoutSeconds = PopupTimeoutSeconds,
                CopySavedPathToClipboard = CopySavedPathToClipboard,
                AllowQuickRename = AllowQuickRename,
                DefaultFilenamePattern = DefaultFilenamePattern,
                GenericImageFallbackEnabled = GenericImageFallbackEnabled,
                PopupPosition = PopupPosition,
                PopupThemeMode = PopupThemeMode,
                ClipboardDetectionMode = GenericImageFallbackEnabled
                    ? ClipboardDetectionMode.GenericFallback
                    : ClipboardDetectionMode.Conservative,
                Destinations = Destinations.Select(d => d.ToModel()).ToList()
            };

            await _settingsService.SaveSettingsAsync(settings);

            // Apply startup configuration
            _startupManager.SetStartupEnabled(StartWithWindows);

            StatusMessage = $"Presets and settings saved successfully ({Destinations.Count} folders active).";
            OnPropertyChanged(nameof(StartupPermissionText));
            OnPropertyChanged(nameof(ShowOnboardingGuide));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings from view model", ex);
            StatusMessage = $"Save error: {ex.Message}";
            return false;
        }
    }

    public void MinimizeToTray()
    {
        RequestHideToTray?.Invoke(this, EventArgs.Empty);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
