using System.Windows;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Services;

public class CaptureDetectionService : ICaptureDetectionService
{
    private readonly ISettingsService _settingsService;
    private readonly ImageCaptureService _imageCaptureService;
    private readonly IAppLogger _logger;

    private readonly object _stateLock = new();
    private long _generationCounter;
    private CancellationTokenSource? _debounceCts;
    private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;
    private string _lastFingerprint = string.Empty;
    private DateTimeOffset _lastFingerprintTime = DateTimeOffset.MinValue;
    private bool _isPaused;

    private static readonly TimeSpan DuplicateCooldown = TimeSpan.FromSeconds(2.0);
    private const int DebounceDelayMs = 250;
    private const string ClipboardHistoryItemIdFormat = "ClipboardHistoryItemId";

    public event EventHandler<CaptureCandidate>? ScreenshotDetected;

    public bool IsPaused
    {
        get { lock (_stateLock) return _isPaused; }
    }

    public CaptureDetectionService(
        ISettingsService settingsService,
        ImageCaptureService imageCaptureService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _imageCaptureService = imageCaptureService;
        _logger = logger;
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            _isPaused = true;
            _logger.Info("Capture detection paused.");
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            _isPaused = false;
            _logger.Info("Capture detection resumed.");
        }
    }

    public void TriggerTestCapture(CaptureCandidate? candidate = null)
    {
        if (candidate == null)
        {
            int width = 480, height = 320, stride = width * 4;
            byte[] pixels = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx] = (byte)(60 + (x * 120 / width));     // B
                    pixels[idx + 1] = (byte)(90 + (y * 80 / height));  // G
                    pixels[idx + 2] = 20;                              // R
                    pixels[idx + 3] = 255;                             // A
                }
            }
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();

            long gen;
            lock (_stateLock)
            {
                _generationCounter++;
                gen = _generationCounter;
            }

            candidate = new CaptureCandidate
            {
                Generation = gen,
                CapturedAt = DateTimeOffset.Now,
                Fingerprint = "TEST_CAPTURE_" + Guid.NewGuid().ToString("N")[..8],
                Width = width,
                Height = height,
                ImageSource = bmp,
                RawBytes = pixels,
                ProvenanceReason = "User test capture from Control Center",
                IsProvenScreenshot = true
            };
        }

        _logger.Info($"Test capture triggered: {candidate.Width}x{candidate.Height}");
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted && !Application.Current.Dispatcher.HasShutdownFinished)
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    ScreenshotDetected?.Invoke(this, candidate);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ScreenshotDetected?.Invoke(this, candidate);
                    });
                }
            }
            catch
            {
                ScreenshotDetected?.Invoke(this, candidate);
            }
        }
        else
        {
            ScreenshotDetected?.Invoke(this, candidate);
        }
    }

    public void SuppressNextUpdate(TimeSpan window)
    {
        lock (_stateLock)
        {
            _suppressUntil = DateTimeOffset.Now.Add(window);
            _logger.Debug($"Clipboard updates suppressed until {_suppressUntil:HH:mm:ss.fff}");
        }
    }

    public void NotifyClipboardUpdate()
    {
        lock (_stateLock)
        {
            if (_isPaused) return;

            if (DateTimeOffset.Now < _suppressUntil)
            {
                _logger.Debug("Ignoring clipboard update: within internal suppression window.");
                return;
            }

            _generationCounter++;
            long currentGen = _generationCounter;

            // Debounce rapid events
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (token.IsCancellationRequested) return;

                    await ProcessClipboardEventAsync(currentGen);
                }
                catch (OperationCanceledException)
                {
                    // Expected on debounce
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error in debounced clipboard evaluation", ex);
                }
            }, token);
        }
    }

    private async Task ProcessClipboardEventAsync(long expectedGen)
    {
        lock (_stateLock)
        {
            if (_generationCounter != expectedGen)
            {
                _logger.Debug($"Discarding stale clipboard processing: gen {expectedGen} != current {_generationCounter}");
                return;
            }
        }

        try
        {
            // Inspect clipboard formats on Dispatcher STA thread
            var (isCandidate, provenanceReason, isProven) = await Application.Current.Dispatcher.InvokeAsync(ClassifyClipboardContent);

            if (!isCandidate)
            {
                _logger.Debug($"Clipboard content rejected: {provenanceReason}");
                return;
            }

            // Capture image
            var candidate = await _imageCaptureService.TryCaptureFromClipboardAsync(expectedGen, provenanceReason, isProven);
            if (candidate == null)
            {
                _logger.Debug("No valid image could be extracted from clipboard.");
                return;
            }

            // Check generation again
            lock (_stateLock)
            {
                if (_generationCounter != expectedGen)
                {
                    _logger.Debug("Discarding captured image: newer clipboard event occurred during extraction.");
                    return;
                }

                // Deduplicate by fingerprint within cooldown
                var now = DateTimeOffset.Now;
                if (!string.IsNullOrEmpty(candidate.Fingerprint) &&
                    candidate.Fingerprint == _lastFingerprint &&
                    (now - _lastFingerprintTime) < DuplicateCooldown)
                {
                    _logger.Debug($"Duplicate image suppressed by fingerprint within cooldown: {candidate.Fingerprint[..8]}...");
                    return;
                }

                _lastFingerprint = candidate.Fingerprint;
                _lastFingerprintTime = now;
            }

            _logger.Info($"Valid screenshot detected! Dimensions: {candidate.Width}x{candidate.Height}, Provenance: {candidate.ProvenanceReason}");

            // Dispatch event to UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                ScreenshotDetected?.Invoke(this, candidate);
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Error processing clipboard event", ex);
        }
    }

    public (bool isCandidate, string reason, bool isProven) ClassifyClipboardContent()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null)
            {
                return (false, "DataObject is null", false);
            }

            var formats = data.GetFormats();
            if (formats == null || formats.Length == 0)
            {
                return (false, "No clipboard formats present", false);
            }

            bool hasFileDrop = data.GetDataPresent(DataFormats.FileDrop);

            // Check if there is an image format
            bool hasImageFormat = data.GetDataPresent(DataFormats.Bitmap) ||
                                  formats.Any(f => f.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Equals("Format17", StringComparison.OrdinalIgnoreCase) || // CF_DIBV5
                                                   f.Equals("PNG", StringComparison.OrdinalIgnoreCase));

            var settings = _settingsService.CurrentSettings;
            return ClassifyClipboardFormats(formats, hasFileDrop, hasImageFormat, settings);
        }
        catch (Exception ex)
        {
            _logger.Warning("Error inspecting clipboard formats", ex);
            return (false, $"Inspection error: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Pure format-based classification, separated from clipboard access for testability.
    /// </summary>
    public static (bool isCandidate, string reason, bool isProven) ClassifyClipboardFormats(
        IReadOnlyCollection<string> formats,
        bool hasFileDrop,
        bool hasImageFormat,
        AppSettings settings)
    {
        // If it's a file copy (e.g. copying an existing image file from Explorer), reject
        if (hasFileDrop)
        {
            return (false, "Content is FileDrop (existing file copied)", false);
        }

        if (!hasImageFormat)
        {
            return (false, "No bitmap or image format present", false);
        }

        // Pasting an item from Windows clipboard history restores the bitmap with this
        // system format. A fresh copy (including Snipping Tool captures) does not have it.
        // Reject regardless of detection mode so history replays never appear as new captures.
        if (formats.Any(f => f.Equals(ClipboardHistoryItemIdFormat, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "Rejected: bitmap restored from Windows clipboard history", false);
        }

        // Check for web browser image copy markers
        bool hasBrowserMarkers = formats.Any(f =>
            f.Equals("HTML Format", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("UniformResourceLocatorW", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("UniformResourceLocator", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("SourceURL", StringComparison.OrdinalIgnoreCase));

        // Check for Office document markers
        bool hasOfficeMarkers = formats.Any(f =>
            f.Equals("Rich Text Format", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("Embed Source", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("Object Descriptor", StringComparison.OrdinalIgnoreCase));

        bool conservative = settings.ClipboardDetectionMode == ClipboardDetectionMode.Conservative;

        if (conservative)
        {
            if (hasBrowserMarkers)
            {
                return (false, "Rejected: contains browser HTML/URL formats (web copy)", false);
            }

            if (hasOfficeMarkers)
            {
                return (false, "Rejected: contains Office document embed formats", false);
            }

            // Windows Snipping Tool / Win+Shift+S / PrtScn characteristic:
            // Has DIB/Bitmap, does NOT have browser/office markers.
            // May have "CanIncludeInClipboardHistory" or standard DIB.
            bool hasHistoryFormat = formats.Any(f => f.Equals("CanIncludeInClipboardHistory", StringComparison.OrdinalIgnoreCase));

            string reason = hasHistoryFormat
                ? "Windows Snipping / Shell capture (CanIncludeInClipboardHistory present)"
                : "Standalone bitmap capture (clean DIB without document/browser markers)";

            return (true, reason, true);
        }
        else
        {
            // GenericFallback mode
            if (settings.GenericImageFallbackEnabled)
            {
                return (true, "Generic image fallback accepted", false);
            }
            else
            {
                // Conservative check failed and fallback is disabled
                if (hasBrowserMarkers || hasOfficeMarkers)
                {
                    return (false, "Rejected by conservative policy (fallback disabled)", false);
                }
                return (true, "Clean image format accepted", false);
            }
        }
    }
}
