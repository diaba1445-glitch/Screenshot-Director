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
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
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
                ResetTimeout();
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
        set { _isSaving = value; OnPropertyChanged(); }
    }

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

        _timeoutSeconds = settings.PopupTimeoutSeconds > 0 ? settings.PopupTimeoutSeconds : 7;
        _remainingTimeMs = _timeoutSeconds * 1000;
        TimeoutProgress = 1.0;

        Filename = _saveService.GenerateDefaultFilename(settings.DefaultFilenamePattern);

        Destinations.Clear();
        foreach (var dest in settings.Destinations.Where(d => d.IsEnabled).OrderBy(d => d.ShortcutNumber))
        {
            Destinations.Add(new DestinationItemViewModel(dest));
        }

        StartTimer();
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
        if (IsSaving) return;

        _remainingTimeMs -= TimerTickIntervalMs;
        if (_timeoutSeconds > 0)
        {
            TimeoutProgress = Math.Clamp((double)_remainingTimeMs / (_timeoutSeconds * 1000), 0.0, 1.0);
        }

        if (_remainingTimeMs <= 0)
        {
            StopTimer();
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
            var settings = _settingsService.CurrentSettings;
            var result = await _saveService.SaveImageAsync(
                PreviewImage,
                folder.Path,
                Filename,
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
