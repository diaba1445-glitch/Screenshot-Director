using System.Windows;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Platform;
using ScreenshotDirector.Services;
using ScreenshotDirector.ViewModels;
using ScreenshotDirector.Views;

namespace ScreenshotDirector;

public partial class App : System.Windows.Application
{
    private IAppLogger? _logger;
    private ISingleInstanceManager? _singleInstanceManager;
    private ISettingsService? _settingsService;
    private IStartupManager? _startupManager;
    private NotificationService? _notificationService;
    private IImageSaveService? _saveService;
    private ImageCaptureService? _captureService;
    private ICaptureDetectionService? _detectionService;
    private IClipboardListener? _clipboardListener;
    private TrayIconManager? _trayIconManager;

    private CapturePopupView? _activePopup;
    private SettingsWindow? _settingsWindow;
    private readonly object _popupLock = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize Logger
        _logger = new AppLogger();
        _logger.Info("Screenshot Director starting up...");

        // 2. Global Unhandled Exception Handlers
        SetupExceptionHandling();

        // 3. Single Instance Check
        _singleInstanceManager = new SingleInstanceManager(_logger);
        if (!_singleInstanceManager.TryAcquireSingleInstance(out bool isPrimary))
        {
            _logger.Info("Secondary instance exiting.");
            Shutdown(0);
            return;
        }

        _singleInstanceManager.SecondInstanceLaunched += (s, args) =>
        {
            Dispatcher.Invoke(OpenSettingsWindow);
        };

        try
        {
            // 4. Initialize Settings
            _settingsService = new SettingsService(_logger);
            var settings = await _settingsService.LoadSettingsAsync();

            // 5. Initialize Startup Registration
            _startupManager = new StartupManager(_logger);
            if (settings.StartWithWindows && !_startupManager.IsStartupEnabled())
            {
                _startupManager.SetStartupEnabled(true);
            }

            // 6. Initialize Services
            _notificationService = new NotificationService(_logger);
            _captureService = new ImageCaptureService(_logger);
            _detectionService = new CaptureDetectionService(_settingsService, _captureService, _logger);
            _saveService = new ImageSaveService(_logger, _detectionService);

            // 7. Initialize Tray Icon
            _trayIconManager = new TrayIconManager(
                _detectionService,
                OpenSettingsWindow,
                ShutdownApp,
                _logger);
            _notificationService.RegisterNotifyIcon(_trayIconManager.NotifyIcon);

            // 8. Initialize Native Clipboard Listener
            _clipboardListener = new ClipboardListener(_logger);
            _clipboardListener.ClipboardUpdated += (s, args) =>
            {
                _detectionService.NotifyClipboardUpdate();
            };
            _clipboardListener.StartListening();

            // 9. Wire Screenshot Detection Event
            _detectionService.ScreenshotDetected += OnScreenshotDetected;

            _logger.Info("Screenshot Director initialized successfully.");

            // If started normally (not via --minimized), show a subtle balloon notification
            bool isMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            if (!isMinimized)
            {
                _notificationService.ShowSuccess(
                    "Screenshot Director Active",
                    "Take a screenshot (Win+Shift+S or PrtScn) to route it to your preset folders.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal exception during application startup", ex);
            System.Windows.MessageBox.Show(
                $"Failed to start Screenshot Director:{Environment.NewLine}{ex.Message}",
                "Screenshot Director Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnScreenshotDetected(object? sender, CaptureCandidate candidate)
    {
        Dispatcher.Invoke(() =>
        {
            lock (_popupLock)
            {
                try
                {
                    // If a popup is already active, close it cleanly first
                    if (_activePopup != null)
                    {
                        try { _activePopup.Close(); } catch { }
                        _activePopup = null;
                    }

                    if (_saveService == null || _notificationService == null || _settingsService == null || _logger == null)
                    {
                        return;
                    }

                    var vm = new CapturePopupViewModel(_saveService, _notificationService, _settingsService, _logger);
                    vm.Initialize(candidate);

                    var popup = new CapturePopupView
                    {
                        DataContext = vm
                    };

                    popup.Closed += (s, e) =>
                    {
                        lock (_popupLock)
                        {
                            if (ReferenceEquals(_activePopup, popup))
                            {
                                _activePopup = null;
                            }
                        }
                    };

                    _activePopup = popup;
                    popup.Show();
                    popup.Activate();
                }
                catch (Exception ex)
                {
                    _logger?.Error("Failed to display CapturePopupView", ex);
                }
            }
        });
    }

    private void OpenSettingsWindow()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (_settingsWindow != null && _settingsWindow.IsLoaded)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                    _settingsWindow.Activate();
                    return;
                }

                if (_settingsService == null || _startupManager == null || _logger == null) return;

                var vm = new SettingsViewModel(_settingsService, _startupManager, _logger);
                _settingsWindow = new SettingsWindow
                {
                    DataContext = vm
                };
                _settingsWindow.Closed += (s, e) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to open SettingsWindow", ex);
            }
        });
    }

    private void ShutdownApp()
    {
        _logger?.Info("Screenshot Director shutting down...");

        try
        {
            _clipboardListener?.StopListening();
            _clipboardListener?.Dispose();
        }
        catch { }

        try
        {
            _trayIconManager?.Dispose();
        }
        catch { }

        try
        {
            _singleInstanceManager?.Dispose();
        }
        catch { }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownApp();
        base.OnExit(e);
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            _logger?.Error("Unhandled AppDomain Exception", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            _logger?.Error("Unhandled Dispatcher Exception", args.Exception);
            args.Handled = true; // Prevent crash on recoverable dispatcher error
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            _logger?.Error("Unobserved Task Exception", args.Exception);
            args.SetObserved();
        };
    }
}
