using System.Windows.Media.Imaging;
using ScreenshotDirector.Core.Models;

namespace ScreenshotDirector.Core.Interfaces;

public interface IImageSaveService
{
    string GenerateDefaultFilename(string pattern);
    bool ValidateFilename(string filename, out string sanitizedOrError);
    string ResolveCollision(string targetDirectory, string baseFilename, CollisionPolicy policy);
    Task<SaveResult> SaveImageAsync(BitmapSource image, string destinationPath, string filename, bool copyPathToClipboard);
}
