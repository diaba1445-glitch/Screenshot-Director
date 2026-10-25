using System.Threading;
using ScreenshotDirector.Core.Interfaces;

namespace ScreenshotDirector.Platform;

public class SingleInstanceManager : ISingleInstanceManager
{
    private const string MutexName = @"Local\ScreenshotDirector_SingleInstance_Mutex";
    private const string EventName = @"Local\ScreenshotDirector_Activate_Event";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registeredWait;
    private readonly IAppLogger _logger;
    private bool _disposed;

    public event EventHandler? SecondInstanceLaunched;

    public SingleInstanceManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool TryAcquireSingleInstance(out bool isPrimaryInstance)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            isPrimaryInstance = createdNew;

            if (isPrimaryInstance)
            {
                // Create named auto-reset event for activation
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    _activateEvent,
                    (state, timedOut) =>
                    {
                        if (!timedOut)
                        {
                            SecondInstanceLaunched?.Invoke(this, EventArgs.Empty);
                        }
                    },
                    null,
                    -1,
                    false);

                _logger.Info("Acquired primary single instance lock.");
                return true;
            }
            else
            {
                _logger.Info("Secondary instance detected. Signaling primary instance.");
                SignalPrimaryInstance();
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error checking single instance mutex", ex);
            isPrimaryInstance = true;
            return true;
        }
    }

    public void SignalPrimaryInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            {
                using (existingEvent)
                {
                    existingEvent.Set();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to signal primary instance via EventWaitHandle", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _registeredWait?.Unregister(null);
            _activateEvent?.Dispose();
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
        catch
        {
            // Ignore during process shutdown
        }
    }
}
