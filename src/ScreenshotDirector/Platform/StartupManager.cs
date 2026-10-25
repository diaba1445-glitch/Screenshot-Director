using System.Diagnostics;
using Microsoft.Win32;
using ScreenshotDirector.Core.Interfaces;

namespace ScreenshotDirector.Platform;

public class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ScreenshotDirector";
    private readonly IAppLogger _logger;

    public StartupManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var currentExe = GetExecutablePath();
            return value.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to check Windows startup registry state", ex);
            return false;
        }
    }

    public bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null)
            {
                _logger.Warning("Unable to open HKCU Run key for write access.");
                return false;
            }

            if (enable)
            {
                var exePath = GetExecutablePath();
                var command = $"\"{exePath}\" --minimized";
                key.SetValue(AppName, command, RegistryValueKind.String);
                _logger.Info($"Registered startup command: {command}");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                    _logger.Info("Unregistered startup command from HKCU Run key.");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set Windows startup state to {enable}", ex);
            return false;
        }
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath)) return processPath;

        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
    }
}
