using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface IAppSettingsService
{
    AppSettingsModel Current { get; }

    event Action<AppSettingsModel>? SettingsChanged;

    void Load();

    bool Save(AppSettingsModel settings, out string error);

    string SettingsPath { get; }
}
