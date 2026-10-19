using System.IO;
using Microsoft.Win32;

namespace RyotaQC.App.Services;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RyotaQCV5";
    private readonly IAppLogger _logger;

    public StartupRegistrationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void DisableStartup()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal menonaktifkan startup otomatis aplikasi.", ex);
        }
    }

    public void EnsureStartupEnabled()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return;
            }

            var expectedValue = $"\"{executablePath}\"";
            var currentValue = runKey.GetValue(RunValueName)?.ToString() ?? string.Empty;

            if (!string.Equals(currentValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                runKey.SetValue(RunValueName, expectedValue, RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal mengaktifkan startup otomatis aplikasi.", ex);
        }
    }
}
