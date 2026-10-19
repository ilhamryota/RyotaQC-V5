using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface IQuickProvisionService
{
    event Action<string, string>? LogRaised;

    QuickProvisionConfig LoadConfig();

    Task<QuickIdentityInfo> GetIdentityAsync();

    Task<bool> ConnectWifiAsync();

    Task<bool> RenameAccountAsync(string newName);

    Task<(bool Success, string Message)> PartitionDiskAsync();

    QuickDriverPlan GetDriverPlan();

    Task<(bool Success, string Message)> InstallVendorDriverAsync();

    /// <summary>Launches Windows Update driver-only search/install as a background process.</summary>
    void StartWindowsUpdateDriverOnly();

    /// <summary>Launches Office setup without waiting for completion.</summary>
    void LaunchOfficeSetup();

    /// <summary>Runs all offline app installers sequentially and reports per-app results.</summary>
    Task<List<QuickAppResult>> InstallOfflineAppsAsync();

    /// <summary>Launches the online AppPack installer without waiting.</summary>
    void LaunchOnlineApps();

    Task<List<string>> GetWallpaperFilesAsync();

    bool SetWallpaper(string filePath);
}
