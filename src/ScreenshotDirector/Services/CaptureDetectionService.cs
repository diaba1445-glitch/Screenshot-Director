using System.IO;
using System.Windows;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.Platform;

namespace ScreenshotDirector.Services;

public class CaptureDetectionService : ICaptureDetectionService
{
    private readonly ISettingsService _settingsService;
    private readonly ImageCaptureService _imageCaptureService;
    private readonly IAppLogger _logger;

    // Apps that commonly put ordinary document/web images on the clipboard as a
    // plain bitmap (often with no HTML/RTF/OLE markers at all). In Conservative
    // mode these must not be mistaken for screenshots. Process names are matched
    // without the ".exe" extension.
    private static readonly HashSet<string> DocumentAndBrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft Office and other document editors
        "WINWORD", "EXCEL", "POWERPNT", "ONENOTE", "MSPUB", "VISIO", "WORDPAD",
        "olk", "soffice", "wps", "wpp", "wpspdf",

        // PDF readers / editors
        "Acrobat", "AcroRd32", "AcroExch", "FoxitPhantomPDF", "FoxitReader",
        "FoxitPDFReader", "FoxitPDFEditor", "SumatraPDF", "PDFXCview", "NitroPDF",
        "NitroPro", "PDFXEdit", "PDF24",

        // Web browsers (their PDF/image viewers can also emit marker-free bitmaps)
        "chrome", "msedge", "MicrosoftEdge", "MicrosoftEdgeCP", "firefox", "brave",
        "opera", "vivaldi", "iexplore", "chromium", "Arc", "waterfox", "librewolf"
    };

    private readonly object _stateLock = new();
    private long _generationCounter;
    private CancellationTokenSource? _debounceCts;
    private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;
    private readonly Dictionary<string, DateTimeOffset> _recentFingerprints = new(StringComparer.Ordinal);
    private bool _isPaused;

    private static readonly TimeSpan DuplicateCooldown = TimeSpan.FromMinutes(2.0);
    private static readonly TimeSpan FingerprintMemory = TimeSpan.FromHours(12.0);
    private const int MaxRememberedFingerprints = 256;
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

            // Capture the clipboard owner at event time, before the debounce delay.
            // Some apps release/flush clipboard ownership within a few hundred ms.
            var sourceProcessName = NativeMethods.GetClipboardOwnerProcessName();
            var foregroundProcessName = NativeMethods.GetForegroundProcessName();

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

                    await ProcessClipboardEventAsync(currentGen, sourceProcessName, foregroundProcessName);
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

    private async Task ProcessClipboardEventAsync(long expectedGen, string? sourceProcessName, string? foregroundProcessName)
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
            var (isCandidate, hasImage, provenanceReason, isProven) = await Application.Current.Dispatcher
                .InvokeAsync(() => ClassifyClipboardContent(sourceProcessName, foregroundProcessName));

            if (!hasImage)
            {
                _logger.Debug($"Clipboard content rejected: {provenanceReason}");
                return;
            }

            // Extract the image even when the source is rejected. Remembering the
            // fingerprint of rejected document images prevents a later paste from
            // re-publishing the same image and raising a popup.
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

                var now = DateTimeOffset.Now;
                PruneRememberedFingerprints(now);

                DateTimeOffset lastSeenAt = default;
                bool seenBefore = !string.IsNullOrEmpty(candidate.Fingerprint) &&
                                  _recentFingerprints.TryGetValue(candidate.Fingerprint, out lastSeenAt);

                if (ShouldSuppressDuplicate(seenBefore, lastSeenAt, now, sourceProcessName))
                {
                    _logger.Debug(
                        $"Duplicate clipboard image suppressed (owner='{sourceProcessName ?? "unknown"}', seen {(now - lastSeenAt).TotalSeconds:F1}s ago): {ShortFingerprint(candidate.Fingerprint)}...");
                    return;
                }

                if (!string.IsNullOrEmpty(candidate.Fingerprint))
                {
                    _recentFingerprints[candidate.Fingerprint] = now;
                }
            }

            if (!isCandidate)
            {
                _logger.Debug($"Clipboard content rejected: {provenanceReason}");
                return;
            }

            _logger.Info($"Valid screenshot detected! Dimensions: {candidate.Width}x{candidate.Height}, Provenance: {candidate.ProvenanceReason}, Fingerprint: {ShortFingerprint(candidate.Fingerprint)}");

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

    /// <summary>
    /// A repeat of an image we have already handled, with no app currently owning
    /// the clipboard, is a clipboard re-publish (typically triggered by pasting into
    /// Word/PDF) — not a fresh screenshot. The cooldown also covers duplicate
    /// updates from an app that still owns the clipboard.
    /// </summary>
    public static bool ShouldSuppressDuplicate(
        bool seenBefore,
        DateTimeOffset lastSeenAt,
        DateTimeOffset now,
        string? sourceProcessName)
    {
        if (!seenBefore)
        {
            return false;
        }

        if (string.IsNullOrEmpty(sourceProcessName))
        {
            return true;
        }

        return (now - lastSeenAt) < DuplicateCooldown;
    }

    private void PruneRememberedFingerprints(DateTimeOffset now)
    {
        var expired = _recentFingerprints
            .Where(kv => (now - kv.Value) > FingerprintMemory)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
        {
            _recentFingerprints.Remove(key);
        }

        while (_recentFingerprints.Count >= MaxRememberedFingerprints)
        {
            var oldest = _recentFingerprints.OrderBy(kv => kv.Value).First().Key;
            _recentFingerprints.Remove(oldest);
        }
    }

    private static string ShortFingerprint(string fingerprint) =>
        string.IsNullOrEmpty(fingerprint) ? "none" : fingerprint[..Math.Min(8, fingerprint.Length)];

    public (bool isCandidate, bool hasImage, string reason, bool isProven) ClassifyClipboardContent(
        string? sourceProcessName,
        string? foregroundProcessName)
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null)
            {
                return (false, false, "DataObject is null", false);
            }

            var formats = data.GetFormats();
            if (formats == null || formats.Length == 0)
            {
                return (false, false, "No clipboard formats present", false);
            }

            bool hasFileDrop = data.GetDataPresent(DataFormats.FileDrop);

            // Check if there is an image format
            bool hasImageFormat = data.GetDataPresent(DataFormats.Bitmap) ||
                                  formats.Any(f => f.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Equals("Format17", StringComparison.OrdinalIgnoreCase) || // CF_DIBV5
                                                   f.Equals("PNG", StringComparison.OrdinalIgnoreCase));

            // File-drop content is an existing file, not a clipboard bitmap we need
            // to remember for paste/re-publish detection.
            bool hasImage = hasImageFormat && !hasFileDrop;

            _logger.Debug(
                $"Clipboard image inspection: owner='{sourceProcessName ?? "unknown"}', " +
                $"foreground='{foregroundProcessName ?? "unknown"}', formats=[{string.Join(", ", formats)}]");

            var settings = _settingsService.CurrentSettings;
            var result = ClassifyClipboardFormats(formats, hasFileDrop, hasImageFormat, settings, sourceProcessName);
            return (result.isCandidate, hasImage, result.reason, result.isProven);
        }
        catch (Exception ex)
        {
            _logger.Warning("Error inspecting clipboard formats", ex);
            return (false, false, $"Inspection error: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Pure format-based classification, separated from clipboard access for testability.
    /// </summary>
    public static (bool isCandidate, string reason, bool isProven) ClassifyClipboardFormats(
        IReadOnlyCollection<string> formats,
        bool hasFileDrop,
        bool hasImageFormat,
        AppSettings settings,
        string? sourceProcessName = null)
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

        bool conservative = settings.ClipboardDetectionMode == ClipboardDetectionMode.Conservative;

        // Some document viewers (notably PDF readers) publish copied images as a bare
        // DIB with no HTML/RTF/OLE markers, making them indistinguishable from a clean
        // screenshot by formats alone. Check the owning application as well so copying
        // an image out of a PDF/Word document never raises the routing popup.
        if (conservative && IsDocumentOrBrowserSource(sourceProcessName))
        {
            return (false, $"Rejected: image copied from document/browser app '{sourceProcessName}'", false);
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
                : $"Standalone bitmap capture (clean DIB; source: {sourceProcessName ?? "unknown"})";

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

    /// <summary>
    /// True when the process is a document editor, PDF viewer, or browser whose
    /// clipboard bitmaps are ordinary document/web images rather than screenshots.
    /// </summary>
    public static bool IsDocumentOrBrowserSource(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = Path.GetFileName(processName.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return DocumentAndBrowserProcessNames.Contains(name);
    }
}
