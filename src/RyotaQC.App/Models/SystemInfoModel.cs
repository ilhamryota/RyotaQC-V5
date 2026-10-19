namespace RyotaQC.App.Models;

public sealed class SystemInfoModel
{
    public string OsName { get; init; } = "N/A";

    public string OsVersion { get; init; } = "N/A";

    public string SystemModel { get; init; } = "N/A";

    public string LoggedInUser { get; init; } = "N/A";

    public string BiosSerialNumber { get; init; } = "N/A";

    public string RamTotal { get; init; } = "N/A";

    public string CpuName { get; init; } = "N/A";

    public string StorageSummary { get; init; } = "N/A";

    public string LcdInfo { get; init; } = "N/A";

    public string VgaInfo { get; init; } = "N/A";

    // ------------------------------------------------------------------ discrete

    /// <summary>Friendly display name (e.g. "IlhamRyota") without DOMAIN\ prefix.</summary>
    public string UserName { get; init; } = "N/A";

    /// <summary>Total installed RAM size, e.g. "16.0 GB".</summary>
    public string RamInstalled { get; init; } = "N/A";

    /// <summary>Physical memory slots populated, e.g. "2".</summary>
    public string RamSlotsUsed { get; init; } = "N/A";

    /// <summary>Free physical memory slots, e.g. "2".</summary>
    public string RamSlotsFree { get; init; } = "N/A";

    /// <summary>Total physical memory slots, e.g. "4".</summary>
    public string RamTotalSlots { get; init; } = "N/A";

    /// <summary>Memory speed label, e.g. "2133 MHz".</summary>
    public string RamSpeed { get; init; } = "N/A";

    /// <summary>Memory technology, e.g. "DDR4".</summary>
    public string RamTechnology { get; init; } = "N/A";

    /// <summary>Whether the installed memory is (partly) onboard: "Ya" / "Tidak".</summary>
    public string RamOnboard { get; init; } = "N/A";

    /// <summary>CPU clock label, e.g. "2.60GHz".</summary>
    public string CpuClock { get; init; } = string.Empty;

    /// <summary>Storage detail summary filled from WMI (per-drive).</summary>
    public string StorageDetail { get; init; } = string.Empty;

    /// <summary>Display resolution + tier, e.g. "1920 x 1080 (FHD)".</summary>
    public string DisplayDetail { get; init; } = string.Empty;

    // ------------------------------------------------------------------ structured

    /// <summary>Structured per-drive storage list (size, model, interface, volumes).</summary>
    public IReadOnlyList<StorageDriveInfo> StorageDrives { get; init; } = Array.Empty<StorageDriveInfo>();

    /// <summary>Structured GPU adapters (integrated/dedicated, VRAM, resolution).</summary>
    public IReadOnlyList<DisplayAdapterInfo> DisplayAdapters { get; init; } = Array.Empty<DisplayAdapterInfo>();

    /// <summary>Primary monitor resolution / refresh / bit depth.</summary>
    public MonitorInfo? PrimaryMonitor { get; init; }

    /// <summary>One-line summary of which GPU drives the display (e.g. "Intel Iris Xe (Integrated)").</summary>
    public string DisplayGpuSummary { get; init; } = string.Empty;
}

public sealed class StorageDriveInfo
{
    public int Index { get; init; }

    public string Model { get; init; } = "Unknown Disk";

    public string FriendlyName { get; init; } = string.Empty;

    public string MediaType { get; init; } = "Disk";      // SSD / HDD / Unknown

    public string Interface { get; init; } = "Unknown";   // NVMe / SATA (Gen) / etc

    public string FormFactor { get; init; } = "Unknown";  // M.2 NVMe / 2.5" SATA / etc

    public string SerialNumber { get; init; } = string.Empty;

    public ulong SizeBytes { get; init; }

    public string SizeText { get; init; } = "N/A";

    public List<StorageVolumeInfo> Volumes { get; init; } = new();
}

public sealed class StorageVolumeInfo
{
    public string DriveLetter { get; init; } = string.Empty; // "C"

    public string Label { get; init; } = "Local Disk";

    public string FileSystem { get; init; } = string.Empty;

    public ulong SizeBytes { get; init; }

    public ulong FreeBytes { get; init; }

    public string SizeText { get; init; } = "N/A";

    public string FreeText { get; init; } = "N/A";

    public double UsedPct { get; init; }
}

public sealed class DisplayAdapterInfo
{
    public string Name { get; init; } = "N/A";

    public string VramText { get; init; } = "N/A";

    /// <summary>"Integrated" / "Dedicated" / "Unknown".</summary>
    public string AdapterType { get; init; } = "Unknown";

    public string DriverVersion { get; init; } = string.Empty;

    /// <summary>Resolution currently driven by this adapter, e.g. "1920 x 1080 @ 60 Hz".</summary>
    public string CurrentResolution { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }
}

public sealed class MonitorInfo
{
    public int Width { get; init; }

    public int Height { get; init; }

    public int RefreshRate { get; init; }

    public int BitsPerPixel { get; init; }

    public string ResolutionText { get; init; } = "N/A";

    public string Tier { get; init; } = string.Empty;
}
