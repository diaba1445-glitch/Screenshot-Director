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
    private MainWindow? _mainWindow;
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
            Dispatcher.Invoke(OpenMainWindow);
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
                OpenMainWindow,
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

            // If started manually (not via --minimized), open the Main Control Dashboard
            bool isMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            if (!isMinimized)
            {
                OpenMainWindow();
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

    private void OpenMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (_mainWindow != null && _mainWindow.IsLoaded)
                {
                    if (_mainWindow.Visibility != Visibility.Visible)
                    {
                        _mainWindow.Show();
                    }
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                    _mainWindow.Topmost = true;
                    _mainWindow.Topmost = false;
                    _mainWindow.Focus();
                    return;
                }

                if (_settingsService == null || _startupManager == null || _logger == null) return;

                var vm = new MainControlViewModel(
                    _settingsService,
                    _startupManager,
                    _detectionService,
                    _saveService,
                    ShutdownApp,
                    _logger);

                _mainWindow = new MainWindow
                {
                    DataContext = vm
                };
                _mainWindow.Closed += (s, e) => _mainWindow = null;
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
                _mainWindow.Focus();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to open MainWindow", ex);
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
