using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppLogger _logger;
    private readonly string _workspaceRoot;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AppSettingsService(IAppLogger logger)
    {
        _logger = logger;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        SettingsPath = ResolveSettingsPath();
    }

    public AppSettingsModel Current { get; private set; } = new();

    public string SettingsPath { get; }

    public event Action<AppSettingsModel>? SettingsChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var raw = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettingsModel>(raw, _jsonOptions) ?? new AppSettingsModel();
            }
            else
            {
                Current = new AppSettingsModel();
                Save(Current, out _);
            }

            ApplyAccentColor(Current.AccentColor);
            SettingsChanged?.Invoke(Current);
            _logger.Info("App settings loaded.");
        }
        catch (Exception ex)
        {
            Current = new AppSettingsModel();
            _logger.Error("Gagal load appsettings.json", ex);
        }
    }

    public bool Save(AppSettingsModel settings, out string error)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(SettingsPath, json);
            Current = settings;
            ApplyAccentColor(settings.AccentColor);
            SettingsChanged?.Invoke(Current);
            _logger.Info("App settings saved.");
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal menyimpan appsettings.json", ex);
            error = ex.Message;
            return false;
        }
    }

    private static void ApplyAccentColor(string colorValue)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorValue);
            Application.Current.Resources["NeonBlueBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["NeonBlueColor"] = color;
        }
        catch
        {
            // Keep existing accent resources if invalid.
        }
    }

    private string ResolveSettingsPath()
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
        if (File.Exists(runtimePath))
        {
            return runtimePath;
        }

        return Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Config", "appsettings.json");
    }
}
