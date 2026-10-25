using System.IO;
using ScreenshotDirector.Infrastructure;
using ScreenshotDirector.Platform;
using Xunit;

namespace ScreenshotDirector.UnitTests;

public class StartupAndSingleInstanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger _logger;

    public StartupAndSingleInstanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SD_StartupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = new AppLogger(Path.Combine(_tempDir, "Logs"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void StartupManager_CanReadAndSetStartupStateSafely()
    {
        var startup = new StartupManager(_logger);
        bool initial = startup.IsStartupEnabled();

        // Testing toggling safely without breaking user's permanent settings
        bool success = startup.SetStartupEnabled(false);
        Assert.True(success);
        Assert.False(startup.IsStartupEnabled());

        // Restore initial state if it was enabled
        if (initial)
        {
            startup.SetStartupEnabled(true);
        }
    }

    [Fact]
    public void SingleInstanceManager_FirstInstanceAcquires_SecondInstanceSignals()
    {
        string testMutex = $@"Local\SD_Test_Mutex_{Guid.NewGuid():N}";
        string testEvent = $@"Local\SD_Test_Event_{Guid.NewGuid():N}";

        using var instance1 = new SingleInstanceManager(_logger, testMutex, testEvent);
        bool firstAcquired = instance1.TryAcquireSingleInstance(out bool isPrimary1);
        Assert.True(firstAcquired);
        Assert.True(isPrimary1);

        using var instance2 = new SingleInstanceManager(_logger, testMutex, testEvent);
        bool secondAcquired = instance2.TryAcquireSingleInstance(out bool isPrimary2);
        Assert.False(secondAcquired);
        Assert.False(isPrimary2);
    }
}
