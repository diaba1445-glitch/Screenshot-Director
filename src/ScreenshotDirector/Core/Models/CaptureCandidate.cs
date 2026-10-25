using System.Windows.Media.Imaging;

namespace ScreenshotDirector.Core.Models;

public class CaptureCandidate
{
    public long Generation { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string Fingerprint { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public BitmapSource? ImageSource { get; init; }
    public byte[] RawBytes { get; init; } = Array.Empty<byte>();
    public string ProvenanceReason { get; init; } = string.Empty;
    public bool IsProvenScreenshot { get; init; }
}

public enum SaveErrorType
{
    None,
    DestinationNotFound,
    WriteAccessDenied,
    DiskFull,
    InvalidFilename,
    PathCopyFailed,
    EncodingError,
    Unknown
}

public class SaveResult
{
    public bool Success { get; init; }
    public string? SavedFilePath { get; init; }
    public bool CopiedPathToClipboard { get; init; }
    public SaveErrorType ErrorType { get; init; } = SaveErrorType.None;
    public string? ErrorMessage { get; init; }

    public static SaveResult Succeeded(string path, bool pathCopied) => new()
    {
        Success = true,
        SavedFilePath = path,
        CopiedPathToClipboard = pathCopied
    };

    public static SaveResult Failed(SaveErrorType type, string message) => new()
    {
        Success = false,
        ErrorType = type,
        ErrorMessage = message
    };
}
