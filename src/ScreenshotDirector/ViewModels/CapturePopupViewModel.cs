using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.ViewModels;

public class DestinationItemViewModel : INotifyPropertyChanged
{
    public DestinationFolder Folder { get; }
    public int ShortcutNumber => Folder.ShortcutNumber;
    public string DisplayName => Folder.Name;
    public string Path => Folder.Path;
    public bool Exists => Folder.Exists;

    public string DisplayIcon
    {
        get
        {
            var text = (DisplayName + " " + Path).ToLowerInvariant();
            if (text.Contains("screenshot") || text.Contains("snip") || text.Contains("capture")) return "📸";
            if (text.Contains("desktop")) return "🖥️";
            if (text.Contains("download")) return "📥";
            if (text.Contains("doc") || text.Contains("work") || text.Contains("report")) return "📑";
            if (text.Contains("picture") || text.Contains("photo") || text.Contains("image")) return "🖼️";
            if (text.Contains("code") || text.Contains("dev") || text.Contains("project") || text.Contains("git")) return "💻";
            if (text.Contains("music") || text.Contains("audio")) return "🎵";
            if (text.Contains("video") || text.Contains("movie")) return "🎬";
            return "📁";
        }
    }

    public DestinationItemViewModel(DestinationFolder folder)
    {
        Folder = folder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CapturePopupViewModel : INotifyPropertyChanged
{
    private readonly IImageSaveService _saveService;
    private readonly INotificationService _notificationService;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;

    private DispatcherTimer? _countdownTimer;
    private int _timeoutSeconds;
    private int _remainingTimeMs;
    private const int TimerTickIntervalMs = 100;

    private BitmapSource? _previewImage;
    private string _filename = string.Empty;
    private string _defaultFilename = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isTimerPaused;
    private bool _isPointerOverWindow;
    private bool _isEditingFilename;
    private bool _isManuallyPaused;
    private bool _isCollapsed;
    private bool _setAsSessionDefaultOnSave;

    public string ResolutionText => PreviewImage != null
        ? $"{PreviewImage.PixelWidth} × {PreviewImage.PixelHeight}"
        : string.Empty;

    public PopupPosition Position => _settingsService?.CurrentSettings?.PopupPosition ?? PopupPosition.BottomRight;

    public PopupThemeMode ThemeMode => _settingsService?.CurrentSettings?.PopupThemeMode ?? PopupThemeMode.LightGlass;
    public bool IsLightGlass => ThemeMode == PopupThemeMode.LightGlass;
    public bool IsDarkGlass => ThemeMode == PopupThemeMode.DarkGlass;

    public string DefaultFilename
    {
        get => _defaultFilename;
        private set { _defaultFilename = value; OnPropertyChanged(); }
    }

    public bool IsTimerPaused
    {
        get => _isTimerPaused;
        private set { _isTimerPaused = value; OnPropertyChanged(); }
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set { _isCollapsed = value; OnPropertyChanged(); }
    }

    public bool HasSessionDefault => !string.IsNullOrWhiteSpace(_settingsService?.SessionDefaultPath) && Directory.Exists(_settingsService?.SessionDefaultPath);
    public string? SessionDefaultPath => _settingsService?.SessionDefaultPath;
    public string SessionDefaultFolderName => !string.IsNullOrWhiteSpace(SessionDefaultPath)
        ? Path.GetFileName(SessionDefaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : string.Empty;

    public bool SetAsSessionDefaultOnSave
    {
        get => _setAsSessionDefaultOnSave;
        set { _setAsSessionDefaultOnSave = value; OnPropertyChanged(); }
    }

    private bool _isCustomPathSelected;
    public bool IsCustomPathSelected
    {
        get => _isCustomPathSelected;
        set { _isCustomPathSelected = value; OnPropertyChanged(); }
    }

    private string? _selectedCustomPath;
    public string? SelectedCustomPath
    {
        get => _selectedCustomPath;
        set { _selectedCustomPath = value; OnPropertyChanged(); }
    }

    private bool _addCustomPathToPresets = true;
    public bool AddCustomPathToPresets
    {
        get => _addCustomPathToPresets;
        set { _addCustomPathToPresets = value; OnPropertyChanged(); }
    }

    private bool _hasError;
    private bool _isSaving;
    private double _timeoutProgress = 1.0;

    public event EventHandler? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        set { _previewImage = value; OnPropertyChanged(); }
    }

    public string Filename
    {
        get => _filename;
        set
        {
            if (_filename != value)
            {
                _filename = value;
                OnPropertyChanged();
                // As soon as user starts renaming, pause the countdown timer!
                PauseTimer();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
    }

    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (_isSaving != value)
            {
                _isSaving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotSaving));
            }
        }
    }

    public bool IsNotSaving => !_isSaving;

    public double TimeoutProgress
    {
        get => _timeoutProgress;
        set { _timeoutProgress = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DestinationItemViewModel> Destinations { get; } = new();

    public CapturePopupViewModel(
        IImageSaveService saveService,
        INotificationService notificationService,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _saveService = saveService;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void Initialize(CaptureCandidate candidate)
    {
        PreviewImage = candidate.ImageSource;
        var settings = _settingsService.CurrentSettings;

        DefaultFilename = _saveService.GenerateDefaultFilename(settings.DefaultFilenamePattern);
        Filename = string.Empty; // Start empty as requested!
        _isPointerOverWindow = false;
        _isEditingFilename = false;
        _isManuallyPaused = false;
        IsTimerPaused = false;
        SetAsSessionDefaultOnSave = false;

        OnPropertyChanged(nameof(HasSessionDefault));
        OnPropertyChanged(nameof(SessionDefaultPath));
        OnPropertyChanged(nameof(SessionDefaultFolderName));

        if (HasSessionDefault)
        {
            IsCollapsed = true;
            _timeoutSeconds = 3; // quick 3-second toast when session default is active
        }
        else
        {
            IsCollapsed = false;
            _timeoutSeconds = settings.PopupTimeoutSeconds > 0 ? settings.PopupTimeoutSeconds : 7;
        }

        _remainingTimeMs = _timeoutSeconds * 1000;
        TimeoutProgress = 1.0;

        Destinations.Clear();
        foreach (var dest in settings.Destinations.Where(d => d.IsEnabled).OrderBy(d => d.ShortcutNumber))
        {
            Destinations.Add(new DestinationItemViewModel(dest));
        }
        OnPropertyChanged(nameof(HasNoDestinations));

        StartTimer();
    }

    public bool HasNoDestinations => Destinations.Count == 0;

    public void OnPointerEnterWindow()
    {
        _isPointerOverWindow = true;
        UpdateTimerPausedState();
    }

    public void OnPointerLeaveWindow()
    {
        _isPointerOverWindow = false;
        UpdateTimerPausedState();
    }

    public void SetEditingFilename(bool isEditing)
    {
        _isEditingFilename = isEditing;
        UpdateTimerPausedState();
    }

    public void PauseTimer()
    {
        _isManuallyPaused = true;
        UpdateTimerPausedState();
    }

    public void ResumeTimer()
    {
        _isManuallyPaused = false;
        _isEditingFilename = false;
        UpdateTimerPausedState();
    }

    public void ToggleTimerPause()
    {
        if (IsTimerPaused)
            ResumeTimer();
        else
            PauseTimer();
    }

    private void UpdateTimerPausedState()
    {
        IsTimerPaused = _isPointerOverWindow || _isEditingFilename || _isManuallyPaused;
    }

    public void ExpandToFullList()
    {
        IsCollapsed = false;
        PauseTimer();
    }

    public void ClearSessionDefault()
    {
        _settingsService.SessionDefaultPath = null;
        OnPropertyChanged(nameof(HasSessionDefault));
        OnPropertyChanged(nameof(SessionDefaultPath));
        OnPropertyChanged(nameof(SessionDefaultFolderName));
        IsCollapsed = false;
        PauseTimer();
    }

    public void ResetTimeout()
    {
        _remainingTimeMs = _timeoutSeconds * 1000;
        TimeoutProgress = 1.0;
    }

    private void StartTimer()
    {
        StopTimer();
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimerTickIntervalMs)
        };
        _countdownTimer.Tick += OnTimerTick;
        _countdownTimer.Start();
    }

    private void StopTimer()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnTimerTick;
            _countdownTimer = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsSaving || IsTimerPaused) return;

        _remainingTimeMs -= TimerTickIntervalMs;
        if (_timeoutSeconds > 0)
        {
            TimeoutProgress = Math.Clamp((double)_remainingTimeMs / (_timeoutSeconds * 1000), 0.0, 1.0);
        }

        if (_remainingTimeMs <= 0)
        {
            StopTimer();
            if (IsCollapsed && HasSessionDefault && !string.IsNullOrWhiteSpace(SessionDefaultPath))
            {
                _logger.Info($"Session default auto-saving to: {SessionDefaultPath}");
                _ = TriggerSaveAsync(new DestinationFolder
                {
                    Name = SessionDefaultFolderName,
                    Path = SessionDefaultPath
                });
                return;
            }

            _logger.Debug("Popup auto-dismissed due to inactivity timeout.");
            Dismiss();
        }
    }

    public async Task<bool> TriggerSaveAsync(DestinationFolder folder)
    {
        if (IsSaving || PreviewImage == null) return false;

        StopTimer();
        IsSaving = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Saving to {folder.Name}...";

        try
        {
            if (SetAsSessionDefaultOnSave && !string.IsNullOrWhiteSpace(folder.Path))
            {
                _settingsService.SessionDefaultPath = folder.Path;
            }

            var settings = _settingsService.CurrentSettings;
            string targetFilename = !string.IsNullOrWhiteSpace(Filename)
                ? (Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || Filename.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? Filename
                    : $"{Filename}.png")
                : DefaultFilename;

            var result = await _saveService.SaveImageAsync(
                PreviewImage,
                folder.Path,
                targetFilename,
                settings.CopySavedPathToClipboard);

            if (result.Success)
            {
                string fileName = Path.GetFileName(result.SavedFilePath ?? Filename);
                string message = result.CopiedPathToClipboard
                    ? $"Saved as {fileName} (Path copied to clipboard)"
                    : $"Saved as {fileName}";

                _notificationService.ShowSuccess("Screenshot Saved", message, result.SavedFilePath);
                RequestClose?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else
            {
                HasError = true;
                ErrorMessage = result.ErrorMessage ?? "Unknown save failure.";
                StatusMessage = "Save failed. You can retry or choose another folder.";
                IsSaving = false;
                ResetTimeout();
                StartTimer();
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Exception in popup save flow", ex);
            HasError = true;
            ErrorMessage = ex.Message;
            StatusMessage = "Save failed unexpectedly.";
            IsSaving = false;
            ResetTimeout();
            StartTimer();
            return false;
        }
    }

    public async Task<bool> SaveToCustomPathAsync(string customPath, bool addToPresets = false, bool setAsSessionDefault = false)
    {
        if (string.IsNullOrWhiteSpace(customPath) || !Directory.Exists(customPath)) return false;

        if (setAsSessionDefault)
        {
            _settingsService.SessionDefaultPath = customPath;
        }

        if (addToPresets)
        {
            try
            {
                var settings = _settingsService.CurrentSettings;
                if (!settings.Destinations.Any(d => d.Path.Equals(customPath, StringComparison.OrdinalIgnoreCase)))
                {
                    int nextNum = 1;
                    while (settings.Destinations.Any(d => d.ShortcutNumber == nextNum) && nextNum <= 9)
                    {
                        nextNum++;
                    }

                    settings.Destinations.Add(new DestinationFolder
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = Path.GetFileName(customPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        Path = customPath,
                        ShortcutNumber = nextNum <= 9 ? nextNum : 0,
                        IsEnabled = true
                    });
                    await _settingsService.SaveSettingsAsync(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to add custom path to presets", ex);
            }
        }

        string folderName = Path.GetFileName(customPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return await TriggerSaveAsync(new DestinationFolder { Name = folderName, Path = customPath });
    }

    public async Task<bool> HandleShortcutKeyAsync(int number)
    {
        ResetTimeout();
        var target = Destinations.FirstOrDefault(d => d.ShortcutNumber == number);
        if (target != null)
        {
            return await TriggerSaveAsync(target.Folder);
        }
        return false;
    }

    public void Dismiss()
    {
        StopTimer();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
