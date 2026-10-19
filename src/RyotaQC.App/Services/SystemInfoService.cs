using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class SystemInfoService : ISystemInfoService
{
    private const int EnumCurrentSettings = -1;
    private readonly IAppLogger _logger;

    public SystemInfoService(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<SystemInfoModel> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var osName = GetWmiValue("Win32_OperatingSystem", "Caption") ?? "N/A";
            var osVersion = GetWmiValue("Win32_OperatingSystem", "Version") ?? Environment.OSVersion.VersionString;
            var biosSerial = GetWmiValue("Win32_BIOS", "SerialNumber") ?? "N/A";
            var cpuName = GetWmiValue("Win32_Processor", "Name") ?? "N/A";
            var systemModel = GetWmiValue("Win32_ComputerSystem", "Model") ?? "N/A";

            var ramText = BuildRamSummary();
            var ramInfo = QueryRamDetail();
            var onboardLabel = GetRamOnboardLabel();
            var storageSummary = BuildStorageSummary();
            var storageDetail = BuildStorageDetail();
            var storageDrives = BuildStorageDrives();
            var lcdInfo = GetLcdInfo();
            var displayDetail = BuildDisplayDetail();
            var vgaInfo = GetVgaInfo();
            var displayAdapters = BuildDisplayAdapters();
            var primaryMonitor = BuildPrimaryMonitor();
            var displayGpuSummary = BuildDisplayGpuSummary(displayAdapters);
            var (friendlyUser, cpuClock) = ResolveFriendlyUserAndCpuClock(cpuName);

            return new SystemInfoModel
            {
                OsName = osName,
                OsVersion = osVersion,
                SystemModel = systemModel,
                LoggedInUser = $"{Environment.UserDomainName}\\{Environment.UserName}",
                UserName = friendlyUser,
                BiosSerialNumber = biosSerial,
                RamTotal = ramText,
                RamInstalled = ramInfo.Installed,
                RamSlotsUsed = ramInfo.SlotsUsed,
                RamSlotsFree = ramInfo.SlotsFree,
                RamTotalSlots = ramInfo.TotalSlots,
                RamSpeed = ramInfo.Speed,
                RamTechnology = ramInfo.Technology,
                RamOnboard = onboardLabel,
                CpuName = cpuName,
                CpuClock = cpuClock,
                StorageSummary = storageSummary,
                StorageDetail = storageDetail,
                LcdInfo = lcdInfo,
                VgaInfo = vgaInfo,
                DisplayDetail = displayDetail,
                StorageDrives = storageDrives,
                DisplayAdapters = displayAdapters,
                PrimaryMonitor = primaryMonitor,
                DisplayGpuSummary = displayGpuSummary
            };
        }, cancellationToken);
    }

    private string BuildRamSummary()
    {
        var ramTotalGb = 0d;
        var installedSlots = 0;
        var detectedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var detectedSpeeds = new List<int>();

        try
        {
            using var memorySearcher = new ManagementObjectSearcher(
                "SELECT Capacity, SMBIOSMemoryType, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");

            foreach (var memory in memorySearcher.Get().Cast<ManagementObject>())
            {
                var bytes = ParseUlong(memory["Capacity"]);
                if (bytes > 0)
                {
                    ramTotalGb += bytes / 1024d / 1024d / 1024d;
                    installedSlots++;
                }

                var typeValue = ParseInt(memory["SMBIOSMemoryType"]?.ToString());
                var ddrType = MapDdrType(typeValue);
                if (!string.IsNullOrWhiteSpace(ddrType))
                {
                    detectedTypes.Add(ddrType);
                }

                var speed = ParseInt(memory["ConfiguredClockSpeed"]?.ToString());
                if (speed <= 0)
                {
                    speed = ParseInt(memory["Speed"]?.ToString());
                }

                if (speed > 0)
                {
                    detectedSpeeds.Add(speed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca detail RAM via WMI", ex);
        }

        var totalSlots = GetTotalMemorySlots();
        var availableSlots = totalSlots > 0 ? Math.Max(totalSlots - installedSlots, 0) : 0;

        var ddrInfo = detectedTypes.Count > 0 ? string.Join("/", detectedTypes.OrderBy(x => x)) : "DDR N/A";
        var speedInfo = detectedSpeeds.Count > 0 ? $"{detectedSpeeds.Max()} MHz" : "N/A";
        var perSlotGb = installedSlots > 0 ? ramTotalGb / installedSlots : 0d;
        var availableSlotGb = availableSlots > 0 && perSlotGb > 0 ? availableSlots * perSlotGb : 0d;
        var onboardInfo = DetectOnboardMemory();

        if (ramTotalGb <= 0)
        {
            return "N/A";
        }

        var summary = new StringBuilder();
        summary.AppendLine($"Ram Terinstall = {ramTotalGb:F1} GB");
        summary.AppendLine($"Slot Tersedia = {availableSlots} Slot ({(availableSlotGb > 0 ? $"{availableSlotGb:F1} GB est." : "N/A")})");
        summary.AppendLine($"Slot Terpasang = {installedSlots} Slot ({ramTotalGb:F1} GB total)");
        summary.Append($"Speed = {speedInfo} ({ddrInfo})");

        if (!string.IsNullOrWhiteSpace(onboardInfo))
        {
            summary.AppendLine();
            summary.Append($"Keterangan = {onboardInfo}");
        }

        return summary.ToString();
    }

    private int GetTotalMemorySlots()
    {
        try
        {
            var total = 0;
            using var slotSearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            foreach (var item in slotSearcher.Get().Cast<ManagementObject>())
            {
                total += ParseInt(item["MemoryDevices"]?.ToString());
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca total memory slot", ex);
            return 0;
        }
    }

    private RamDetailInfo QueryRamDetail()
    {
        double totalGb = 0;
        var sticks = 0;
        var detectedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var speeds = new List<int>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");

            foreach (var memory in searcher.Get().Cast<ManagementObject>())
            {
                var bytes = ParseUlong(memory["Capacity"]);
                if (bytes > 0)
                {
                    totalGb += bytes / 1024d / 1024d / 1024d;
                    sticks++;
                }

                var typeValue = ParseInt(memory["SMBIOSMemoryType"]?.ToString());
                var ddrType = MapDdrType(typeValue);
                if (!string.IsNullOrWhiteSpace(ddrType))
                {
                    detectedTypes.Add(ddrType);
                }

                var speed = ParseInt(memory["ConfiguredClockSpeed"]?.ToString());
                if (speed <= 0)
                {
                    speed = ParseInt(memory["Speed"]?.ToString());
                }

                if (speed > 0)
                {
                    speeds.Add(speed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca RAM detail", ex);
        }

        var totalSlots = GetTotalMemorySlots();
        if (totalSlots < sticks)
        {
            totalSlots = sticks;
        }

        var freeSlots = Math.Max(totalSlots - sticks, 0);
        var technology = detectedTypes.Count > 0
            ? string.Join("/", detectedTypes.OrderBy(x => x))
            : "DDR";
        var speedText = speeds.Count > 0 ? $"{speeds.Max()} MHz" : "N/A";

        return new RamDetailInfo
        {
            Installed = totalGb > 0 ? $"{totalGb:F1} GB" : "N/A",
            SlotsUsed = $"{sticks}",
            SlotsFree = $"{freeSlots}",
            TotalSlots = $"{totalSlots}",
            Speed = speedText,
            Technology = technology
        };
    }

    private string GetRamOnboardLabel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceLocator, FormFactor FROM Win32_PhysicalMemory");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var locator = item["DeviceLocator"]?.ToString() ?? string.Empty;
                var formFactor = ParseInt(item["FormFactor"]?.ToString());
                if (locator.Contains("onboard", StringComparison.OrdinalIgnoreCase) ||
                    locator.Contains("on board", StringComparison.OrdinalIgnoreCase) ||
                    formFactor == 11)
                {
                    return "Ya";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal deteksi RAM onboard", ex);
        }

        return "Tidak";
    }

    private string BuildDisplayDetail()
    {
        try
        {
            var mode = new DevMode
            {
                DeviceName = string.Empty,
                FormName = string.Empty,
                Size = (short)Marshal.SizeOf<DevMode>()
            };

            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                var width = (int)mode.PelsWidth;
                var height = (int)mode.PelsHeight;
                if (width > 0 && height > 0)
                {
                    var tier = MapResolutionTier(width, height);
                    var tierShort = tier
                        .Replace("(UHD / 4K)", "4K")
                        .Replace("(QHD / 2K)", "2K")
                        .Replace("(FHD)", "FHD")
                        .Replace("(HD+)", "HD+")
                        .Replace("(HD)", "HD");
                    return $"{width} x {height} · {tierShort}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca resolusi display", ex);
        }

        return "N/A";
    }

    private string BuildStorageDetail()
    {
        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT Index, Model, Size, MediaType, InterfaceType, PNPDeviceID FROM Win32_DiskDrive");

            var disks = diskSearcher.Get()
                .Cast<ManagementObject>()
                .OrderBy(x => ParseInt(x["Index"]?.ToString()))
                .ToList();

            if (disks.Count == 0)
            {
                return "N/A";
            }

            var lines = disks
                .Select(disk =>
                {
                    var model = disk["Model"]?.ToString() ?? "Disk";
                    var size = FormatGb(ParseUlong(disk["Size"]), 0);
                    var mediaType = ResolveSimpleMediaType(disk);
                    var connection = ResolveSimpleConnection(disk);
                    return $"{mediaType} {connection} — {size} GB";
                })
                .ToList();

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca storage detail", ex);
            return "N/A";
        }
    }

    private static string ResolveSimpleMediaType(ManagementObject disk)
    {
        var meta = disk["MediaType"]?.ToString() ?? string.Empty;
        var model = disk["Model"]?.ToString() ?? string.Empty;
        var raw = $"{model} {meta}".ToLowerInvariant();

        if (raw.Contains("ssd") || raw.Contains("solid state") || raw.Contains("nvme"))
        {
            return "SSD";
        }

        if (raw.Contains("hdd") || raw.Contains("hard disk"))
        {
            return "HDD";
        }

        return "Disk";
    }

    private static string ResolveSimpleConnection(ManagementObject disk)
    {
        var pnp = disk["PNPDeviceID"]?.ToString() ?? string.Empty;
        var iface = disk["InterfaceType"]?.ToString() ?? string.Empty;
        var raw = $"{pnp} {iface}".ToLowerInvariant();

        if (raw.Contains("nvme"))
        {
            return "M.2 NVMe";
        }

        if (raw.Contains("sata"))
        {
            return "SATA";
        }

        return string.IsNullOrWhiteSpace(iface) ? "N/A" : iface;
    }

    private List<StorageDriveInfo> BuildStorageDrives()
    {
        var result = new List<StorageDriveInfo>();

        try
        {
            var physicalMetadata = GetPhysicalDiskMetadata();

            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Model, Size, MediaType, InterfaceType, PNPDeviceID, SerialNumber FROM Win32_DiskDrive");

            var remainingMetadata = new List<StoragePhysicalMetadata>(physicalMetadata);

            var diskObjects = diskSearcher.Get().Cast<ManagementObject>().ToList();

            var disks = diskObjects
                .Select(item => new PhysicalDiskInfo
                {
                    DeviceId = item["DeviceID"]?.ToString() ?? string.Empty,
                    Index = ParseInt(item["Index"]?.ToString()),
                    Model = item["Model"]?.ToString() ?? "Unknown Disk",
                    MediaType = item["MediaType"]?.ToString() ?? string.Empty,
                    SizeBytes = ParseUlong(item["Size"]),
                    InterfaceType = item["InterfaceType"]?.ToString() ?? string.Empty,
                    PnpDeviceId = item["PNPDeviceID"]?.ToString() ?? string.Empty,
                    SerialNumber = item["SerialNumber"]?.ToString() ?? string.Empty
                })
                .OrderBy(x => x.Index)
                .ToList();

            foreach (var disk in disks)
            {
                var metadata = ResolvePhysicalMetadata(disk, remainingMetadata);
                if (metadata is not null)
                {
                    remainingMetadata.Remove(metadata);
                }

                var diskObj = diskObjects.FirstOrDefault(d => string.Equals(
                    d["DeviceID"]?.ToString(),
                    disk.DeviceId,
                    StringComparison.OrdinalIgnoreCase));
                var volumes = GetLogicalVolumesForDisk(diskObj!);
                var totalDiskBytes = volumes.Count > 0
                    ? volumes.Aggregate<LogicalVolumeInfo, ulong>(0, (acc, v) => acc + v.SizeBytes)
                    : disk.SizeBytes;
                var mediaType = ResolveMediaTypeLabel(disk, metadata);
                var connectionType = ResolveConnectionTypeLabel(disk, metadata);
                var formFactor = ResolveFormFactorLabel(disk, metadata, connectionType);

                var volumeInfos = volumes
                    .OrderBy(x => x.DeviceId, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new StorageVolumeInfo
                    {
                        DriveLetter = v.DeviceId.Replace(":", string.Empty),
                        Label = string.IsNullOrWhiteSpace(v.VolumeName) ? "Local Disk" : v.VolumeName,
                        FileSystem = v.FileSystem,
                        SizeBytes = v.SizeBytes,
                        FreeBytes = v.FreeBytes,
                        SizeText = FormatGb(v.SizeBytes, 1) + " GB",
                        FreeText = FormatGb(v.FreeBytes, 1) + " GB",
                        UsedPct = v.SizeBytes > 0
                            ? Math.Round(((double)(v.SizeBytes - v.FreeBytes) / v.SizeBytes) * 100d, 1)
                            : 0d
                    })
                    .ToList();

                result.Add(new StorageDriveInfo
                {
                    Index = disk.Index,
                    Model = disk.Model,
                    FriendlyName = metadata?.FriendlyName ?? disk.Model,
                    MediaType = mediaType,
                    Interface = connectionType,
                    FormFactor = formFactor,
                    SerialNumber = disk.SerialNumber,
                    SizeBytes = totalDiskBytes,
                    SizeText = FormatGb(totalDiskBytes, 1) + " GB",
                    Volumes = volumeInfos
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca storage drives terstruktur", ex);
        }

        return result;
    }

    private List<DisplayAdapterInfo> BuildDisplayAdapters()
    {
        var result = new List<DisplayAdapterInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, VideoProcessor, AdapterDACType, InstalledDisplayDrivers, PNPDeviceID FROM Win32_VideoController");

            foreach (var adapter in searcher.Get().Cast<ManagementObject>())
            {
                var name = adapter["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var adapterRamBytes = ParseUlong(adapter["AdapterRAM"]);
                var vramText = adapterRamBytes > 0 ? FormatMemory(adapterRamBytes) : "Shared";
                var adapterType = ClassifyGpu(name, adapter);
                var driverVersion = adapter["DriverVersion"]?.ToString() ?? string.Empty;

                var w = ParseInt(adapter["CurrentHorizontalResolution"]?.ToString());
                var h = ParseInt(adapter["CurrentVerticalResolution"]?.ToString());
                var refresh = ParseInt(adapter["CurrentRefreshRate"]?.ToString());
                var resolution = (w > 0 && h > 0)
                    ? $"{w} x {h}" + (refresh > 0 ? $" @ {refresh} Hz" : string.Empty)
                    : string.Empty;

                result.Add(new DisplayAdapterInfo
                {
                    Name = name,
                    VramText = vramText,
                    AdapterType = adapterType,
                    DriverVersion = driverVersion,
                    CurrentResolution = resolution,
                    IsPrimary = result.Count == 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca display adapters terstruktur", ex);
        }

        return result;
    }

    /// <summary>
    /// Heuristic GPU classification: Intel/AMD integrated silicon naming -> Integrated,
    /// NVIDIA / discrete AMD naming -> Dedicated.
    /// </summary>
    private static string ClassifyGpu(string name, ManagementObject adapter)
    {
        var raw = (name ?? string.Empty).ToLowerInvariant();
        var pnp = (adapter["PNPDeviceID"]?.ToString() ?? string.Empty).ToLowerInvariant();

        // NVIDIA adapters are always dedicated.
        if (raw.Contains("nvidia") || raw.Contains("geforce") || raw.Contains("rtx") ||
            raw.Contains("gtx") || raw.Contains("quadro") || raw.Contains("tesla"))
        {
            return "Dedicated";
        }

        // AMD dedicated GPUs.
        if (raw.Contains("radeon rx") || raw.Contains("radeon pro") || raw.Contains("radeon vii") ||
            raw.Contains("firepro") || raw.Contains("radeon hd 5") || raw.Contains("radeon hd 7"))
        {
            return "Dedicated";
        }

        // Intel integrated families.
        if (raw.Contains("intel"))
        {
            if (raw.Contains("arc") || raw.Contains("iris xe max"))
            {
                return "Dedicated";
            }

            return "Integrated";
        }

        // AMD integrated (Radeon Graphics / Vega mobile APUs).
        if (raw.Contains("amd") || raw.Contains("radeon"))
        {
            if (raw.Contains("radeon graphics") || raw.Contains("vega") && raw.Contains("graphics") ||
                raw.Contains("radeon(tm) graphics"))
            {
                return "Integrated";
            }

            // APUs reported as "AMD Radeon" without discrete naming usually integrated.
            if (raw.Contains("radeon") && !raw.Contains("rx") && !raw.Contains("pro"))
            {
                return "Integrated";
            }
        }

        // Microsoft Basic Render / virtual -> Unknown.
        if (raw.Contains("microsoft") || raw.Contains("basic render"))
        {
            return "Unknown";
        }

        return "Unknown";
    }

    private MonitorInfo? BuildPrimaryMonitor()
    {
        try
        {
            var mode = new DevMode
            {
                DeviceName = string.Empty,
                FormName = string.Empty,
                Size = (short)Marshal.SizeOf<DevMode>()
            };

            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                var width = (int)mode.PelsWidth;
                var height = (int)mode.PelsHeight;
                if (width > 0 && height > 0)
                {
                    var tier = MapResolutionTier(width, height);
                    var refresh = (int)mode.DisplayFrequency;
                    var bpp = (int)mode.BitsPerPel;
                    return new MonitorInfo
                    {
                        Width = width,
                        Height = height,
                        RefreshRate = refresh,
                        BitsPerPixel = bpp,
                        ResolutionText = $"{width} x {height}" + (refresh > 0 ? $" @ {refresh} Hz" : string.Empty),
                        Tier = tier
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca monitor utama", ex);
        }

        return null;
    }

    private static string BuildDisplayGpuSummary(IReadOnlyList<DisplayAdapterInfo> adapters)
    {
        if (adapters.Count == 0)
        {
            return "N/A";
        }

        var primary = adapters.FirstOrDefault(a => a.IsPrimary) ?? adapters[0];
        return string.IsNullOrWhiteSpace(primary.AdapterType) || primary.AdapterType == "Unknown"
            ? primary.Name
            : $"{primary.Name} ({primary.AdapterType})";
    }

    private (string FriendlyUser, string CpuClock) ResolveFriendlyUserAndCpuClock(string cpuName)
    {
        var friendlyUser = Environment.UserName;
        try
        {
            var accountName = GetWmiValue("Win32_ComputerSystem", "UserName");
            if (!string.IsNullOrWhiteSpace(accountName))
            {
                foreach (var separator in new[] { '\\', '/', ';', '-' })
                {
                    var idx = accountName.LastIndexOf(separator);
                    if (idx >= 0 && idx < accountName.Length - 1)
                    {
                        friendlyUser = accountName.Substring(idx + 1);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca nama user ramah", ex);
        }

        var clock = string.Empty;
        if (!string.IsNullOrWhiteSpace(cpuName))
        {
            var atIdx = cpuName.LastIndexOf(" @ ", StringComparison.OrdinalIgnoreCase);
            if (atIdx >= 0 && atIdx < cpuName.Length - 1)
            {
                clock = cpuName.Substring(atIdx + 3).Trim();
            }
        }

        return (friendlyUser, clock);
    }

    private string DetectOnboardMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceLocator, FormFactor FROM Win32_PhysicalMemory");
            var hasOnboardKeyword = false;
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var locator = item["DeviceLocator"]?.ToString() ?? string.Empty;
                var formFactor = ParseInt(item["FormFactor"]?.ToString());
                if (locator.Contains("onboard", StringComparison.OrdinalIgnoreCase) ||
                    locator.Contains("on board", StringComparison.OrdinalIgnoreCase) ||
                    formFactor == 11)
                {
                    hasOnboardKeyword = true;
                    break;
                }
            }

            if (!hasOnboardKeyword)
            {
                return string.Empty;
            }

            var totalSlots = GetTotalMemorySlots();
            return totalSlots > 0
                ? $"Onboard memory terdeteksi, slot tambahan tersedia: {totalSlots} slot total."
                : "Onboard memory terdeteksi, slot tambahan tidak terdeteksi.";
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal deteksi onboard memory", ex);
            return string.Empty;
        }
    }

    private string BuildStorageSummary()
    {
        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Model, Size, MediaType, InterfaceType, PNPDeviceID, SerialNumber FROM Win32_DiskDrive");

            var physicalMetadata = GetPhysicalDiskMetadata();
            var remainingMetadata = new List<StoragePhysicalMetadata>(physicalMetadata);

            var diskObjects = diskSearcher.Get().Cast<ManagementObject>().ToList();

            var disks = diskObjects
                .Select(item => new PhysicalDiskInfo
                {
                    DeviceId = item["DeviceID"]?.ToString() ?? string.Empty,
                    Index = ParseInt(item["Index"]?.ToString()),
                    Model = item["Model"]?.ToString() ?? "Unknown Disk",
                    MediaType = item["MediaType"]?.ToString() ?? string.Empty,
                    SizeBytes = ParseUlong(item["Size"]),
                    InterfaceType = item["InterfaceType"]?.ToString() ?? string.Empty,
                    PnpDeviceId = item["PNPDeviceID"]?.ToString() ?? string.Empty,
                    SerialNumber = item["SerialNumber"]?.ToString() ?? string.Empty
                })
                .OrderBy(x => x.Index)
                .ToList();

            if (disks.Count == 0)
            {
                return "N/A";
            }

            var output = new StringBuilder();

            for (var i = 0; i < disks.Count; i++)
            {
                var disk = disks[i];
                var metadata = ResolvePhysicalMetadata(disk, remainingMetadata);
                if (metadata is not null)
                {
                    remainingMetadata.Remove(metadata);
                }

                var diskObj = diskObjects.FirstOrDefault(d => string.Equals(
                    d["DeviceID"]?.ToString(),
                    disk.DeviceId,
                    StringComparison.OrdinalIgnoreCase));
                var volumes = GetLogicalVolumesForDisk(diskObj!);
                var totalDiskBytes = volumes.Count > 0
                    ? volumes.Aggregate<LogicalVolumeInfo, ulong>(0, (acc, v) => acc + v.SizeBytes)
                    : disk.SizeBytes;
                var mediaType = ResolveMediaTypeLabel(disk, metadata);
                var connectionType = ResolveConnectionTypeLabel(disk, metadata);
                var formFactor = ResolveFormFactorLabel(disk, metadata, connectionType);

                output.AppendLine($"Disk {disk.Index}");
                output.AppendLine($"Nama Disk = {disk.Model}");
                output.AppendLine($"Jenis Disk = {mediaType}");
                output.AppendLine($"Interface = {connectionType}");
                output.AppendLine($"Form Factor = {formFactor}");
                output.AppendLine($"Total Disk = {FormatGb(totalDiskBytes, 2)} GB");

                if (volumes.Count == 0)
                {
                    // No logical volume mapping found; keep disk summary only.
                }
                else
                {
                    foreach (var volume in volumes.OrderBy(x => x.DeviceId, StringComparer.OrdinalIgnoreCase))
                    {
                        var volumeName = string.IsNullOrWhiteSpace(volume.VolumeName) ? "Local Disk" : volume.VolumeName;
                        var driveLetter = volume.DeviceId.Replace(":", string.Empty);
                        output.AppendLine(
                            $"{volumeName} ( {driveLetter} ) = {FormatGb(volume.SizeBytes, 2)} GB ({(string.IsNullOrWhiteSpace(volume.FileSystem) ? "N/A" : volume.FileSystem)})");
                    }

                    output.Length -= Environment.NewLine.Length;
                }

                if (i < disks.Count - 1)
                {
                    output.AppendLine();
                    output.AppendLine();
                }
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca storage summary detail", ex);
            return "N/A";
        }
    }

    private List<StoragePhysicalMetadata> GetPhysicalDiskMetadata()
    {
        var result = new List<StoragePhysicalMetadata>();

        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            var query = new ObjectQuery("SELECT FriendlyName, Model, SerialNumber, MediaType, BusType, Size, PhysicalLocation FROM MSFT_PhysicalDisk");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                result.Add(new StoragePhysicalMetadata
                {
                    FriendlyName = item["FriendlyName"]?.ToString() ?? string.Empty,
                    Model = item["Model"]?.ToString() ?? string.Empty,
                    SerialNumber = item["SerialNumber"]?.ToString() ?? string.Empty,
                    MediaType = ParseInt(item["MediaType"]?.ToString()),
                    BusType = ParseInt(item["BusType"]?.ToString()),
                    SizeBytes = ParseUlong(item["Size"]),
                    PhysicalLocation = item["PhysicalLocation"]?.ToString() ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca metadata disk dari MSFT_PhysicalDisk", ex);
        }

        return result;
    }

    private StoragePhysicalMetadata? ResolvePhysicalMetadata(PhysicalDiskInfo disk, List<StoragePhysicalMetadata> metadata)
    {
        if (metadata.Count == 0)
        {
            return null;
        }

        var diskSerial = NormalizeComparableText(disk.SerialNumber);
        if (!string.IsNullOrWhiteSpace(diskSerial))
        {
            var serialMatch = metadata.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.SerialNumber) &&
                NormalizeComparableText(m.SerialNumber) == diskSerial);
            if (serialMatch is not null)
            {
                return serialMatch;
            }
        }

        var diskModel = NormalizeComparableText(disk.Model);
        var modelMatches = metadata
            .Where(m =>
            {
                var metaText = NormalizeComparableText($"{m.Model} {m.FriendlyName}");
                return !string.IsNullOrWhiteSpace(metaText) &&
                       (metaText.Contains(diskModel, StringComparison.Ordinal) ||
                        diskModel.Contains(metaText, StringComparison.Ordinal));
            })
            .OrderBy(m => GetSizeDistance(disk.SizeBytes, m.SizeBytes))
            .ToList();

        if (modelMatches.Count > 0)
        {
            return modelMatches[0];
        }

        var sizeMatch = metadata
            .OrderBy(m => GetSizeDistance(disk.SizeBytes, m.SizeBytes))
            .FirstOrDefault();

        if (sizeMatch is not null && IsSizeClose(disk.SizeBytes, sizeMatch.SizeBytes))
        {
            return sizeMatch;
        }

        return null;
    }

    private static double GetSizeDistance(ulong left, ulong right)
    {
        return Math.Abs((double)left - right);
    }

    private static bool IsSizeClose(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return false;
        }

        var diff = Math.Abs((double)left - right);
        var max = Math.Max((double)left, right);
        return diff / max <= 0.03d;
    }

    private static string NormalizeComparableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToUpperInvariant();
    }

    private string ResolveMediaTypeLabel(PhysicalDiskInfo disk, StoragePhysicalMetadata? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.MediaType == 4)
            {
                return "SSD";
            }

            if (metadata.MediaType == 3)
            {
                return "HDD";
            }

            if (metadata.MediaType == 5)
            {
                return "SCM";
            }
        }

        var guess = $"{disk.Model} {disk.MediaType}".ToLowerInvariant();
        if (guess.Contains("nvme") || guess.Contains("ssd") || guess.Contains("solid state"))
        {
            return "SSD";
        }

        if (guess.Contains("hdd") || guess.Contains("hard disk") || guess.Contains("7200rpm") || guess.Contains("5400rpm"))
        {
            return "HDD";
        }

        return "Unknown";
    }

    private string ResolveConnectionTypeLabel(PhysicalDiskInfo disk, StoragePhysicalMetadata? metadata)
    {
        var busType = metadata is not null ? MapBusType(metadata.BusType) : string.Empty;
        if (!string.IsNullOrWhiteSpace(busType) && !busType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            if (busType.Equals("SATA", StringComparison.OrdinalIgnoreCase))
            {
                var sataGen = DetectSataGeneration(disk, metadata);
                return string.IsNullOrWhiteSpace(sataGen) ? $"{busType} (Gen N/A)" : $"{busType} ({sataGen})";
            }

            return busType;
        }

        var source = $"{disk.Model} {disk.PnpDeviceId} {disk.InterfaceType}".ToLowerInvariant();
        if (source.Contains("nvme"))
        {
            return "NVMe";
        }

        if (source.Contains("sata"))
        {
            var sataGen = DetectSataGeneration(disk, metadata);
            return string.IsNullOrWhiteSpace(sataGen) ? "SATA (Gen N/A)" : $"SATA ({sataGen})";
        }

        return string.IsNullOrWhiteSpace(disk.InterfaceType) ? "Unknown" : disk.InterfaceType;
    }

    private static string DetectSataGeneration(PhysicalDiskInfo disk, StoragePhysicalMetadata? metadata)
    {
        var source = $"{disk.Model} {disk.PnpDeviceId} {metadata?.FriendlyName} {metadata?.Model}".ToLowerInvariant();
        if (source.Contains("sata iii") || source.Contains("sata3") || source.Contains("6gb/s"))
        {
            return "SATA III";
        }

        if (source.Contains("sata ii") || source.Contains("sata2") || source.Contains("3gb/s"))
        {
            return "SATA II";
        }

        if (source.Contains("sata i") || source.Contains("1.5gb/s"))
        {
            return "SATA I";
        }

        return string.Empty;
    }

    private static string ResolveFormFactorLabel(PhysicalDiskInfo disk, StoragePhysicalMetadata? metadata, string connectionLabel)
    {
        var source = $"{disk.Model} {disk.PnpDeviceId} {metadata?.FriendlyName} {metadata?.Model} {metadata?.PhysicalLocation}".ToLowerInvariant();
        var connection = connectionLabel.ToLowerInvariant();

        if (connection.Contains("nvme") || source.Contains("nvme"))
        {
            return "M.2 NVMe";
        }

        if (connection.Contains("sata") || source.Contains("sata"))
        {
            if (source.Contains("m.2") || source.Contains("ngff"))
            {
                return "M.2 SATA";
            }

            if (source.Contains("2.5"))
            {
                return "2.5\" SATA";
            }

            if (source.Contains("3.5"))
            {
                return "3.5\" SATA";
            }

            return "SATA (Form Factor tidak terdeteksi)";
        }

        if (connection.Contains("usb"))
        {
            return "External / USB";
        }

        if (connection.Contains("sas"))
        {
            return "SAS";
        }

        if (connection.Contains("raid"))
        {
            return "RAID";
        }

        return "Unknown";
    }

    private static string MapBusType(int busType)
    {
        return busType switch
        {
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "IEEE 1394",
            5 => "SSA",
            6 => "Fibre Channel",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "MAX",
            15 => "File Backed Virtual",
            16 => "Storage Spaces",
            17 => "NVMe",
            _ => "Unknown"
        };
    }

    private List<LogicalVolumeInfo> GetLogicalVolumesForDisk(ManagementObject diskObject)
    {
        var volumes = new Dictionary<string, LogicalVolumeInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (diskObject is null)
            {
                return [];
            }

            // Use GetRelated() instead of "ASSOCIATORS OF" WQL query.
            // GetRelated() handles the association traversal internally via the
            // WMI COM API, completely avoiding the backslash-escaping pitfalls
            // of WQL string literals for device IDs like "\\.\PHYSICALDRIVE0".
            foreach (var partition in diskObject.GetRelated("Win32_DiskPartition").Cast<ManagementObject>())
            {
                using var partitionObj = partition;

                foreach (var logical in partitionObj.GetRelated("Win32_LogicalDisk").Cast<ManagementObject>())
                {
                    using var logicalObj = logical;

                    var deviceId = logicalObj["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        continue;
                    }

                    var driveType = ParseInt(logicalObj["DriveType"]?.ToString());
                    if (driveType > 0 && driveType != 3)
                    {
                        continue;
                    }

                    volumes[deviceId] = new LogicalVolumeInfo
                    {
                        DeviceId = deviceId,
                        VolumeName = logicalObj["VolumeName"]?.ToString() ?? string.Empty,
                        FileSystem = logicalObj["FileSystem"]?.ToString() ?? string.Empty,
                        SizeBytes = ParseUlong(logicalObj["Size"]),
                        FreeBytes = ParseUlong(logicalObj["FreeSpace"])
                    };
                }
            }
        }
        catch (Exception ex)
        {
            var diskDeviceId = diskObject?["DeviceID"]?.ToString() ?? "?";
            _logger.Error($"Gagal membaca volume untuk disk '{diskDeviceId}'", ex);
        }

        return volumes.Values.ToList();
    }

    private string GetVgaInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            var lines = new List<string>();

            foreach (var adapter in searcher.Get().Cast<ManagementObject>())
            {
                var name = adapter["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var adapterRamBytes = ParseUlong(adapter["AdapterRAM"]);
                var vramText = adapterRamBytes > 0 ? FormatMemory(adapterRamBytes) : "N/A";
                var line = $"{name}, VRAM {vramText}";

                if (!lines.Contains(line, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add(line);
                }
            }

            return lines.Count == 0 ? "N/A" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca VGA info", ex);
            return "N/A";
        }
    }

    private string GetLcdInfo()
    {
        try
        {
            var mode = new DevMode
            {
                DeviceName = string.Empty,
                FormName = string.Empty,
                Size = (short)Marshal.SizeOf<DevMode>()
            };

            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                var width = (int)mode.PelsWidth;
                var height = (int)mode.PelsHeight;
                if (width > 0 && height > 0)
                {
                    return $"Resolusi Layar {width} x {height} {MapResolutionTier(width, height)}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca resolusi layar via WinAPI", ex);
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");

            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var width = ParseInt(item["CurrentHorizontalResolution"]?.ToString());
                var height = ParseInt(item["CurrentVerticalResolution"]?.ToString());
                if (width > 0 && height > 0)
                {
                    return $"Resolusi Layar {width} x {height} {MapResolutionTier(width, height)}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca resolusi layar via WMI", ex);
        }

        return "N/A";
    }

    private static string MapResolutionTier(int width, int height)
    {
        var maxSide = Math.Max(width, height);
        var minSide = Math.Min(width, height);

        if (maxSide >= 3840 || minSide >= 2160)
        {
            return "(UHD / 4K)";
        }

        if (maxSide >= 2560 || minSide >= 1440)
        {
            return "(QHD / 2K)";
        }

        if (maxSide >= 1920 || minSide >= 1080)
        {
            return "(FHD)";
        }

        if (maxSide >= 1600 || minSide >= 900)
        {
            return "(HD+)";
        }

        if (maxSide >= 1366 || minSide >= 768)
        {
            return "(HD)";
        }

        return "(SD)";
    }

    private static string FormatGb(ulong bytes, int decimals = 0)
    {
        return (bytes / 1024d / 1024d / 1024d).ToString($"F{decimals}");
    }

    private static string FormatMemory(ulong bytes)
    {
        var gb = bytes / 1024d / 1024d / 1024d;
        if (gb >= 1)
        {
            return $"{gb:F1} GB";
        }

        var mb = bytes / 1024d / 1024d;
        return $"{mb:F0} MB";
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    private static ulong ParseUlong(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return value switch
        {
            ulong ulongValue => ulongValue,
            long longValue when longValue > 0 => (ulong)longValue,
            uint uintValue => uintValue,
            int intValue when intValue > 0 => (ulong)intValue,
            _ => ulong.TryParse(value.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static string MapDdrType(int smbiosType)
    {
        return smbiosType switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _ => string.Empty
        };
    }

    private string? GetWmiValue(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                return obj[propertyName]?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"WMI query gagal: {className}.{propertyName}", ex);
        }

        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DevMode
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int ICMMethod;
        public int ICMIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }

    private sealed class PhysicalDiskInfo
    {
        public string DeviceId { get; init; } = string.Empty;
        public int Index { get; init; }
        public string Model { get; init; } = string.Empty;
        public string MediaType { get; init; } = string.Empty;
        public ulong SizeBytes { get; init; }
        public string InterfaceType { get; init; } = string.Empty;
        public string PnpDeviceId { get; init; } = string.Empty;
        public string SerialNumber { get; init; } = string.Empty;
    }

    private sealed class LogicalVolumeInfo
    {
        public string DeviceId { get; init; } = string.Empty;
        public string VolumeName { get; init; } = string.Empty;
        public string FileSystem { get; init; } = string.Empty;
        public ulong SizeBytes { get; init; }
        public ulong FreeBytes { get; init; }
    }

    private sealed class StoragePhysicalMetadata
    {
        public string FriendlyName { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string SerialNumber { get; init; } = string.Empty;
        public int MediaType { get; init; }
        public int BusType { get; init; }
        public ulong SizeBytes { get; init; }
        public string PhysicalLocation { get; init; } = string.Empty;
    }

    private sealed class RamDetailInfo
    {
        public string Installed { get; init; } = string.Empty;
        public string SlotsUsed { get; init; } = string.Empty;
        public string SlotsFree { get; init; } = string.Empty;
        public string TotalSlots { get; init; } = string.Empty;
        public string Speed { get; init; } = string.Empty;
        public string Technology { get; init; } = string.Empty;
    }
}
