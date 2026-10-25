using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Interfaces;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Services;

public class ImageSaveService : IImageSaveService
{
    private readonly ICaptureDetectionService? _detectionService;
    private readonly IAppLogger _logger;

    public ImageSaveService(IAppLogger logger, ICaptureDetectionService? detectionService = null)
    {
        _logger = logger;
        _detectionService = detectionService;
    }

    public string GenerateDefaultFilename(string pattern)
    {
        var now = DateTime.Now;
        string timestamp = now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return $"Screenshot_{timestamp}.png";
        }

        if (pattern.Contains("{yyyy-MM-dd_HH-mm-ss}"))
        {
            pattern = pattern.Replace("{yyyy-MM-dd_HH-mm-ss}", timestamp);
        }
        else if (pattern.Contains("yyyy-MM-dd_HH-mm-ss"))
        {
            pattern = pattern.Replace("yyyy-MM-dd_HH-mm-ss", timestamp);
        }
        else
        {
            return $"Screenshot_{timestamp}.png";
        }

        if (!pattern.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            pattern += ".png";
        }

        return pattern;
    }

    public bool ValidateFilename(string filename, out string sanitizedOrError)
    {
        sanitizedOrError = string.Empty;
        if (string.IsNullOrWhiteSpace(filename))
        {
            sanitizedOrError = "Filename cannot be empty.";
            return false;
        }

        // Prevent path traversal
        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\') || filename.Contains(':'))
        {
            sanitizedOrError = "Filename must not contain path separators or traversal characters.";
            return false;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (filename.IndexOfAny(invalidChars) >= 0)
        {
            sanitizedOrError = "Filename contains invalid characters.";
            return false;
        }

        if (!filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".png";
        }

        sanitizedOrError = filename;
        return true;
    }

    public string ResolveCollision(string targetDirectory, string baseFilename, CollisionPolicy policy)
    {
        string fullPath = Path.Combine(targetDirectory, baseFilename);
        if (!File.Exists(fullPath))
        {
            return fullPath;
        }

        if (policy == CollisionPolicy.Reject)
        {
            throw new IOException($"File already exists and collision policy is Reject: {fullPath}");
        }

        string nameWithoutExt = Path.GetFileNameWithoutExtension(baseFilename);
        string ext = Path.GetExtension(baseFilename);

        int counter = 1;
        while (true)
        {
            string candidate = $"{nameWithoutExt} ({counter}){ext}";
            string candidatePath = Path.Combine(targetDirectory, candidate);
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
            counter++;
        }
    }

    public async Task<SaveResult> SaveImageAsync(BitmapSource image, string destinationPath, string filename, bool copyPathToClipboard)
    {
        if (image == null)
        {
            return SaveResult.Failed(SaveErrorType.EncodingError, "Image source is null.");
        }

        if (!ValidateFilename(filename, out string validFilename))
        {
            return SaveResult.Failed(SaveErrorType.InvalidFilename, validFilename);
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return SaveResult.Failed(SaveErrorType.DestinationNotFound, "Destination path is empty.");
        }

        // Ensure directory exists or create it
        try
        {
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Permission denied creating directory: {destinationPath}", ex);
            return SaveResult.Failed(SaveErrorType.WriteAccessDenied, $"Access denied to directory: {destinationPath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed creating directory: {destinationPath}", ex);
            return SaveResult.Failed(SaveErrorType.DestinationNotFound, $"Could not access directory: {destinationPath}");
        }

        // Encode PNG to byte array first in background thread to verify encoding before touching disk
        byte[] pngBytes;
        try
        {
            pngBytes = await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("PNG encoding failed", ex);
            return SaveResult.Failed(SaveErrorType.EncodingError, $"PNG encoding failed: {ex.Message}");
        }

        if (pngBytes.Length == 0)
        {
            return SaveResult.Failed(SaveErrorType.EncodingError, "Encoded PNG byte stream is empty.");
        }

        // Atomic write with collision handling using a single staging file committed via File.Move
        string targetFilePath = string.Empty;
        string tempFilePath = Path.Combine(destinationPath, $".tmp_{Guid.NewGuid():N}.tmp");
        const int maxCollisionAttempts = 100;
        int attempt = 0;
        bool writeSuccess = false;

        try
        {
            // Write to a temporary staging file in the destination folder once
            using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(pngBytes, 0, pngBytes.Length);
                await fs.FlushAsync();
            }

            // Verify temp file
            var tempInfo = new FileInfo(tempFilePath);
            if (tempInfo.Length != pngBytes.Length)
            {
                try { File.Delete(tempFilePath); } catch { }
                return SaveResult.Failed(SaveErrorType.EncodingError, "Incomplete write detected.");
            }

            // Find an available filename and atomically commit the staging file
            while (attempt < maxCollisionAttempts && !writeSuccess)
            {
                string candidateFilename;
                if (attempt == 0)
                {
                    candidateFilename = validFilename;
                }
                else
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(validFilename);
                    string ext = Path.GetExtension(validFilename);
                    candidateFilename = $"{nameWithoutExt} ({attempt}){ext}";
                }

                targetFilePath = Path.Combine(destinationPath, candidateFilename);

                if (File.Exists(targetFilePath))
                {
                    attempt++;
                    continue;
                }

                try
                {
                    File.Move(tempFilePath, targetFilePath, overwrite: false);
                    writeSuccess = true;
                }
                catch (IOException)
                {
                    // Target already exists due to race condition; try next suffix
                    attempt++;
                }
            }

            if (!writeSuccess)
            {
                try { File.Delete(tempFilePath); } catch { }
                return SaveResult.Failed(SaveErrorType.Unknown, "Could not resolve a unique filename after multiple attempts.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            try { File.Delete(tempFilePath); } catch { }
            _logger.Error($"Access denied writing file: {targetFilePath}", ex);
            return SaveResult.Failed(SaveErrorType.WriteAccessDenied, "Access denied to destination folder.");
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            try { File.Delete(tempFilePath); } catch { }
            _logger.Error("Disk full while writing screenshot", ex);
            return SaveResult.Failed(SaveErrorType.DiskFull, "Not enough disk space available.");
        }
        catch (Exception ex)
        {
            try { File.Delete(tempFilePath); } catch { }
            _logger.Error($"File write error for {targetFilePath}", ex);
            return SaveResult.Failed(SaveErrorType.Unknown, $"Save failed: {ex.Message}");
        }

        _logger.Info($"Successfully saved screenshot to: {targetFilePath} ({pngBytes.Length} bytes)");

        // Copy path to clipboard if configured
        bool pathCopied = false;
        if (copyPathToClipboard)
        {
            try
            {
                // Suppress clipboard listener before writing to clipboard
                _detectionService?.SuppressNextUpdate(TimeSpan.FromSeconds(2));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Clipboard.SetText(targetFilePath);
                        pathCopied = true;
                        _logger.Debug($"Copied file path to clipboard: {targetFilePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to copy saved path to clipboard", ex);
                        pathCopied = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Warning("Clipboard path-copy dispatch failed", ex);
                pathCopied = false;
            }
        }

        return SaveResult.Succeeded(targetFilePath, pathCopied);
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
        const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
        return ex.HResult == HR_ERROR_DISK_FULL || ex.HResult == HR_ERROR_HANDLE_DISK_FULL;
    }
}
