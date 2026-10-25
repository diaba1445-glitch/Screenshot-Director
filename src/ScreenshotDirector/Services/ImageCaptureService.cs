using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Services;

public class ImageCaptureService
{
    private readonly IAppLogger _logger;

    public ImageCaptureService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<CaptureCandidate?> TryCaptureFromClipboardAsync(long generation, string provenanceReason, bool isProven)
    {
        // Must run on STA thread for WPF Clipboard access
        return await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            const int maxRetries = 5;
            const int delayMs = 40;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsImage())
                    {
                        return null;
                    }

                    var image = Clipboard.GetImage();
                    if (image == null)
                    {
                        return null;
                    }

                    // Create independent frozen copy
                    var (clonedBitmap, rawBytes, fingerprint) = CreateIndependentCopyAndFingerprint(image);
                    if (clonedBitmap == null)
                    {
                        return null;
                    }

                    return new CaptureCandidate
                    {
                        Generation = generation,
                        CapturedAt = DateTimeOffset.Now,
                        Fingerprint = fingerprint,
                        Width = (int)clonedBitmap.PixelWidth,
                        Height = (int)clonedBitmap.PixelHeight,
                        ImageSource = clonedBitmap,
                        RawBytes = rawBytes,
                        ProvenanceReason = provenanceReason,
                        IsProvenScreenshot = isProven
                    };
                }
                catch (System.Runtime.InteropServices.COMException comEx)
                {
                    // CLIPBRD_E_CANT_OPEN (0x800401D0) - clipboard locked by another process
                    if (attempt == maxRetries)
                    {
                        _logger.Warning($"Clipboard was locked after {maxRetries} attempts", comEx);
                        return null;
                    }
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error retrieving image from clipboard", ex);
                    return null;
                }
            }

            return null;
        }).Task.Unwrap();
    }

    public static (BitmapSource? bitmap, byte[] bytes, string fingerprint) CreateIndependentCopyAndFingerprint(BitmapSource source)
    {
        try
        {
            // Convert to standard Bgr32 or Bgra32 if needed for consistent hashing and encoding
            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixelBytes = new byte[height * stride];
            formatted.CopyPixels(pixelBytes, stride, 0);

            // Compute deterministic SHA256 of pixel bytes
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(pixelBytes);
            string fingerprint = Convert.ToHexString(hash);

            // Create WriteableBitmap detached from any clipboard source
            var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            writeable.WritePixels(new Int32Rect(0, 0, width, height), pixelBytes, stride, 0);
            writeable.Freeze();

            return (writeable, pixelBytes, fingerprint);
        }
        catch
        {
            // Fallback: PNG encode into memory stream
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                byte[] pngBytes = ms.ToArray();

                using var sha = SHA256.Create();
                string fingerprint = Convert.ToHexString(sha.ComputeHash(pngBytes));

                var frame = BitmapFrame.Create(new MemoryStream(pngBytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();

                return (frame, pngBytes, fingerprint);
            }
            catch
            {
                return (null, Array.Empty<byte>(), string.Empty);
            }
        }
    }
}
