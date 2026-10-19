using System.Text.Json.Serialization;

namespace RyotaQC.App.Models;

public sealed class QuickIdentityInfo
{
    public string OsName { get; init; } = "N/A";
    public string OsVersion { get; init; } = "N/A";
    public string Manufacturer { get; init; } = "N/A";
    public string SystemModel { get; init; } = "N/A";
    public string SystemFamily { get; init; } = "N/A";
    public string SerialNumber { get; init; } = "N/A";
    public string CpuName { get; init; } = "N/A";

    public string RamInstalled { get; init; } = "N/A";
    public string RamSlotsUsed { get; init; } = "N/A";
    public string RamSlotsFree { get; init; } = "N/A";
    public string RamSpeed { get; init; } = "N/A";

    public string DiskType { get; init; } = "N/A";
    public string DiskInterface { get; init; } = "N/A";
    public string DiskTotal { get; init; } = "N/A";

    public string LcdInfo { get; init; } = "N/A";
    public string VgaInfo { get; init; } = "N/A";

    /// <summary>Brand-aware human friendly unit name (e.g. "ThinkPad T480").</summary>
    public string UnitName { get; init; } = "N/A";
}

public sealed class QuickWifiProfile
{
    [JsonPropertyName("SSID")]
    public string Ssid { get; set; } = string.Empty;

    [JsonPropertyName("Password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("Priority")]
    public int Priority { get; set; }
}

public sealed class QuickProvisionConfig
{
    [JsonPropertyName("WifiProfiles")]
    public List<QuickWifiProfile> WifiProfiles { get; set; } = [];

    [JsonPropertyName("EnablePartitioning")]
    public bool EnablePartitioning { get; set; } = true;

    [JsonPropertyName("LargeDiskThresholdGB")]
    public int LargeDiskThresholdGB { get; set; } = 200;

    [JsonPropertyName("LargeDataPartitionGB")]
    public int LargeDataPartitionGB { get; set; } = 100;

    [JsonPropertyName("SmallDiskThresholdGB")]
    public int SmallDiskThresholdGB { get; set; } = 100;

    [JsonPropertyName("SmallDataPartitionGB")]
    public int SmallDataPartitionGB { get; set; } = 50;

    [JsonPropertyName("MinimumCFreeAfterPartitionGB")]
    public int MinimumCFreeAfterPartitionGB { get; set; } = 35;
}

public sealed class QuickAppResult
{
    public string Name { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class QuickResultItem
{
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class QuickDriverPlan
{
    public string Brand { get; init; } = "Other";
    public string Description { get; init; } = string.Empty;
    public bool UsesLocalInstaller { get; init; }
    public bool NeedsWindowsUpdateConfirm { get; init; }
}
