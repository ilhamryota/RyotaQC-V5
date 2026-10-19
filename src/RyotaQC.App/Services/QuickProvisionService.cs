using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class QuickProvisionService : IQuickProvisionService
{
    private readonly IAppLogger _logger;
    private readonly string _workspaceRoot;

    public event Action<string, string>? LogRaised;

    public QuickProvisionService(IAppLogger logger)
    {
        _logger = logger;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
    }

    private void Log(string message, string level = "INFO") => LogRaised?.Invoke(message, level);

    // ------------------------------------------------------------------ config

    public QuickProvisionConfig LoadConfig()
    {
        var candidates = new[]
        {
            Path.Combine(_workspaceRoot, "tools", "RyotaQuick", "config.json")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var config = JsonSerializer.Deserialize<QuickProvisionConfig>(File.ReadAllText(path));
                if (config is not null)
                {
                    Log($"Config dimuat: {path}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Gagal membaca config Quick: {path}", ex);
            }
        }

        Log("Config tidak ditemukan, memakai default.", "WARN");
        return new QuickProvisionConfig();
    }

    // --------------------------------------------------------------- identity

    public Task<QuickIdentityInfo> GetIdentityAsync()
    {
        return Task.Run(() =>
        {
            var info = new QuickIdentityInfo
            {
                OsName = QueryScalar("SELECT Caption FROM Win32_OperatingSystem"),
                OsVersion = QueryScalar("SELECT Version FROM Win32_OperatingSystem"),
                Manufacturer = QueryScalar("SELECT Manufacturer FROM Win32_ComputerSystem"),
                SystemModel = QueryScalar("SELECT Model FROM Win32_ComputerSystem"),
                SystemFamily = QueryScalar("SELECT SystemFamily FROM Win32_ComputerSystem"),
                SerialNumber = QueryScalar("SELECT SerialNumber FROM Win32_BIOS"),
                CpuName = QueryScalar("SELECT Name FROM Win32_Processor")
            };

            var ram = QueryRam();
            var disk = QueryDisk();
            var vga = QueryVga();
            var lcd = QueryLcd();

            return new QuickIdentityInfo
            {
                OsName = info.OsName,
                OsVersion = info.OsVersion,
                Manufacturer = info.Manufacturer,
                SystemModel = info.SystemModel,
                SystemFamily = info.SystemFamily,
                SerialNumber = info.SerialNumber,
                CpuName = info.CpuName,
                RamInstalled = ram.Installed,
                RamSlotsUsed = ram.SlotsUsed,
                RamSlotsFree = ram.SlotsFree,
                RamSpeed = ram.Speed,
                DiskType = disk.Type,
                DiskInterface = disk.Interface,
                DiskTotal = disk.Total,
                LcdInfo = lcd,
                VgaInfo = vga,
                UnitName = BuildUnitName(info.Manufacturer, info.SystemModel, info.SystemFamily)
            };
        });
    }

    private static string QueryScalar(string query)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                foreach (var prop in obj.Properties)
                {
                    if (prop.Value is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return "N/A";
    }

    private (string Installed, string SlotsUsed, string SlotsFree, string Speed) QueryRam()
    {
        try
        {
            ulong total = 0;
            var sticks = 0;
            var speed = string.Empty;

            using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory"))
            {
                foreach (var obj in searcher.Get())
                {
                    sticks++;
                    total += Convert.ToUInt64(obj["Capacity"]);
                    if (string.IsNullOrEmpty(speed) && obj["Speed"] is not null)
                    {
                        speed = $"{obj["Speed"]} MHz";
                    }
                }
            }

            var totalSlots = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
            {
                foreach (var obj in searcher.Get())
                {
                    if (obj["MemoryDevices"] is not null)
                    {
                        totalSlots = Convert.ToInt32(obj["MemoryDevices"]);
                        break;
                    }
                }
            }

            if (totalSlots < sticks)
            {
                totalSlots = sticks;
            }

            return ($"{total / (1024UL * 1024UL * 1024UL)} GB", $"{sticks}", $"{totalSlots - sticks}", speed);
        }
        catch
        {
            return ("N/A", "N/A", "N/A", "N/A");
        }
    }

    private (string Type, string Interface, string Total) QueryDisk()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\Microsoft\\Windows\\Storage",
                "SELECT BusType, MediaType, Size, FriendlyName FROM MSFT_PhysicalDisk");

            ulong bestSize = 0;
            var bestType = "N/A";
            var bestInterface = "N/A";

            foreach (var obj in searcher.Get())
            {
                var busType = Convert.ToInt32(obj["BusType"]);
                if (busType is 7 or 13 or 14 or 15 or 16)
                {
                    continue; // USB / SD / virtual
                }

                var mediaType = Convert.ToInt32(obj["MediaType"]);
                var size = Convert.ToUInt64(obj["Size"]);

                var isSsd = mediaType is 4 or 5;
                var (typeName, iface) = busType switch
                {
                    17 => ("M.2 NVMe", "NVMe"),
                    11 => ("SATA 2.5\"", "SATA III"),
                    _ => ("Lainnya", $"Bus {busType}")
                };

                var label = isSsd ? $"SSD {typeName}" : $"HDD {typeName}";
                if (size > bestSize)
                {
                    bestSize = size;
                    bestType = label;
                    bestInterface = iface;
                }
            }

            if (bestSize == 0)
            {
                return ("N/A", "N/A", "N/A");
            }

            return (bestType, bestInterface, $"{Math.Round(bestSize / (1024.0 * 1024.0 * 1024.0))} GB");
        }
        catch
        {
            return ("N/A", "N/A", "N/A");
        }
    }

    private static string QueryVga()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (obj["CurrentHorizontalResolution"] is not null && obj["CurrentVerticalResolution"] is not null)
                {
                    return $"{name} ({obj["CurrentHorizontalResolution"]}x{obj["CurrentVerticalResolution"]})";
                }

                return name;
            }
        }
        catch
        {
            // ignore
        }

        return "N/A";
    }

    private static string QueryLcd()
    {
        var parts = new List<string>();

        // 1. Get monitor friendly name from WMI
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT UserFriendlyName FROM WmiMonitorID");
            foreach (var obj in searcher.Get())
            {
                if (obj["UserFriendlyName"] is byte[] bytes)
                {
                    var name = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        parts.Add(name);
                        break;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        // 2. Get screen resolution via Win32 API (EnumDisplaySettings)
        try
        {
            var mode = new DevMode { DeviceName = string.Empty, FormName = string.Empty, Size = (short)Marshal.SizeOf<DevMode>() };
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                var w = (int)mode.PelsWidth;
                var h = (int)mode.PelsHeight;
                if (w > 0 && h > 0)
                {
                    parts.Add($"{w}x{h}");
                }
            }
        }
        catch
        {
            // ignore
        }

        // 3. Fallback: WMI Win32_VideoController for resolution
        if (parts.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
                foreach (var item in searcher.Get().Cast<ManagementObject>())
                {
                    var w = int.TryParse(item["CurrentHorizontalResolution"]?.ToString(), out var wVal) ? wVal : 0;
                    var h = int.TryParse(item["CurrentVerticalResolution"]?.ToString(), out var hVal) ? hVal : 0;
                    if (w > 0 && h > 0)
                    {
                        parts.Add($"{w}x{h}");
                        break;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return parts.Count > 0 ? string.Join(" ", parts) : "N/A";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    private const int EnumCurrentSettings = -1;

    // ------------------------------------------------------- brand-aware name

    private static bool IsMachineCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}[A-Z0-9]{4,}$"))
        {
            return true;
        }

        return value.Length <= 10 && value.All(char.IsLetterOrDigit) &&
               value.Count(char.IsDigit) >= 4;
    }

    internal static string BuildUnitName(string manufacturer, string model, string family)
    {
        var vendor = "Other";
        if (manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) vendor = "Lenovo";
        else if (manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase)) vendor = "Dell";
        else if (manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase) || manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) vendor = "HP";
        else if (manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase)) vendor = "ASUS";
        else if (manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase)) vendor = "Acer";
        else if (manufacturer.Contains("MSI", StringComparison.OrdinalIgnoreCase) || manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase)) vendor = "MSI";

        var model2 = model.Trim();
        var family2 = family.Trim();

        string StripBrand(string value)
        {
            foreach (var prefix in new[] { "Lenovo ", "HP ", "Hewlett-Packard ", "Dell ", "ASUSTeK ", "ASUS ", "Acer ", "MSI " })
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(prefix.Length);
                }
            }

            return value;
        }

        string Clean(string value) => value
            .Replace("Notebook PC", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Base Model", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var candidate = Clean(StripBrand(model2));

        if (vendor == "Lenovo" && IsMachineCode(candidate))
        {
            candidate = !string.IsNullOrWhiteSpace(family2) ? Clean(StripBrand(family2)) : candidate;
        }

        if (IsMachineCode(candidate) && !string.IsNullOrWhiteSpace(family2))
        {
            candidate = Clean(StripBrand(family2));
        }

        return string.IsNullOrWhiteSpace(candidate) ? model2 : candidate;
    }

    // ------------------------------------------------------------------ wifi

    public async Task<bool> ConnectWifiAsync()
    {
        var config = LoadConfig();
        var profiles = config.WifiProfiles.OrderBy(p => p.Priority).ToList();
        if (profiles.Count == 0)
        {
            Log("Tidak ada profil Wi-Fi di config.", "WARN");
            return false;
        }

        foreach (var profile in profiles)
        {
            Log($"Mencoba Wi-Fi: {profile.Ssid} (prioritas {profile.Priority})...");
            try
            {
                var xml = BuildWifiProfileXml(profile.Ssid, profile.Password);
                var tempPath = Path.Combine(Path.GetTempPath(), $"ryota-wifi-{Guid.NewGuid():N}.xml");
                await File.WriteAllTextAsync(tempPath, xml);

                var addResult = await RunProcessAsync("netsh", $"wlan add profile filename=\"{tempPath}\" user=all", TimeSpan.FromSeconds(15));
                File.Delete(tempPath);

                if (!addResult.Success)
                {
                    Log($"Gagal menambah profil {profile.Ssid}: {addResult.Output}", "WARN");
                    continue;
                }

                var connectResult = await RunProcessAsync("netsh", $"wlan connect name=\"{profile.Ssid}\"", TimeSpan.FromSeconds(15));
                if (!connectResult.Success)
                {
                    Log($"Gagal connect {profile.Ssid}.", "WARN");
                    continue;
                }

                Log($"Terhubung ke {profile.Ssid}. Mengecek internet...");
                if (await WaitForInternetAsync(TimeSpan.FromSeconds(20)))
                {
                    Log($"Internet OK via {profile.Ssid}.", "SUCCESS");
                    return true;
                }

                Log($"Terhubung ke {profile.Ssid} tetapi belum ada internet.", "WARN");
            }
            catch (Exception ex)
            {
                Log($"Error Wi-Fi {profile.Ssid}: {ex.Message}", "ERROR");
            }
        }

        Log("Semua profil Wi-Fi gagal.", "ERROR");
        return false;
    }

    private static string BuildWifiProfileXml(string ssid, string password)
    {
        var escapedSsid = ssid.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var escapedPassword = password.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{escapedSsid}</name>
  <SSIDConfig><SSID><name>{escapedSsid}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>WPA2PSK</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>{escapedPassword}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
</WLANProfile>";
    }

    private static async Task<bool> WaitForInternetAsync(TimeSpan timeout)
    {
        using var ping = new Ping();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(1500);
        }

        return false;
    }

    // ------------------------------------------------------------- rename

    public Task<bool> RenameAccountAsync(string newName)
    {
        return Task.Run(async () =>
        {
            try
            {
                var safeName = newName.Replace("'", "''");
                var script =
                    "$ErrorActionPreference='Stop';\n" +
                    "$account=[ADSI]\"WinNT://$env:COMPUTERNAME/$env:USERNAME,user\";\n" +
                    $"$account.FullName='{safeName}';\n" +
                    "$account.SetInfo();\n" +
                    "Write-Output 'OK'";

                var tempPath = Path.Combine(Path.GetTempPath(), $"ryota-rename-{Guid.NewGuid():N}.ps1");
                await File.WriteAllTextAsync(tempPath, script);

                try
                {
                    var result = await RunProcessAsync("powershell",
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPath}\"",
                        TimeSpan.FromSeconds(30));

                    if (result.Success && result.Output.Contains("OK"))
                    {
                        Log($"Nama akun diubah menjadi: {newName}", "SUCCESS");
                        return true;
                    }

                    Log($"Gagal rename akun: {result.Output}", "ERROR");
                    return false;
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                Log($"Error rename akun: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    // ----------------------------------------------------------- partition

    public Task<(bool Success, string Message)> PartitionDiskAsync()
    {
        return Task.Run(async () =>
        {
            var config = LoadConfig();
            if (!config.EnablePartitioning)
            {
                return (false, "Partisi dinonaktifkan di config.");
            }

            var script = BuildPartitionScript(config);
            var tempPath = Path.Combine(Path.GetTempPath(), $"ryota-part-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(tempPath, script);

            try
            {
                var result = await RunProcessAsync("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPath}\"",
                    TimeSpan.FromMinutes(5));
                var output = result.Output.Trim();
                Log(output, result.ExitCode == 0 ? "INFO" : "WARN");

                return result.ExitCode == 0
                    ? (true, output)
                    : (false, string.IsNullOrWhiteSpace(output) ? "Partisi gagal." : output);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        });
    }

    private static string BuildPartitionScript(QuickProvisionConfig config)
    {
        return $@"
$ErrorActionPreference = 'Stop'
$minCFree = {config.MinimumCFreeAfterPartitionGB}
$largeThreshold = {config.LargeDiskThresholdGB}
$largeSize = {config.LargeDataPartitionGB}
$smallThreshold = {config.SmallDiskThresholdGB}
$smallSize = {config.SmallDataPartitionGB}

$osPartition = Get-Partition -DriveLetter C
$disk = $osPartition | Get-Disk
$diskGB = [math]::Round($disk.Size / 1GB)

if ($diskGB -lt $smallThreshold) {{
    Write-Output 'SKIP: disk < $smallThreshold GB, tidak dibuat partisi.'
    exit 0
}}

$dataGB = if ($diskGB -ge $largeThreshold) {{ $largeSize }} else {{ $smallSize }}

# Skip when a data partition already exists (non-C internal volumes).
$existing = @(Get-Partition -DiskNumber $disk.Number | Where-Object {{ $_.DriveLetter -and $_.DriveLetter -ne 'C' }})
if ($existing.Count -gt 0) {{
    Write-Output ('SKIP: partisi data sudah ada (' + (($existing | ForEach-Object {{ $_.DriveLetter }}) -join ', ') + ').')
    exit 0
}}

$volume = Get-Volume -DriveLetter C
$freeAfterGB = [math]::Floor(($volume.SizeRemaining / 1GB) - $dataGB)
if ($freeAfterGB -lt $minCFree) {{
    Write-Output ""SKIP: sisa free C setelah partisi ($freeAfterGB GB) < minimum $minCFree GB.""
    exit 0
}}

$supported = Get-PartitionSupportedSize -DriveLetter C
$targetSize = $osPartition.Size - ($dataGB * 1GB)
if ($targetSize -gt $supported.SizeMax) {{
    Write-Output 'SKIP: shrink tidak didukung sebesar itu (file tidak bisa dipindah).'
    exit 0
}}

$letter = $null
foreach ($candidate in 'D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z') {{
    $taken = Get-Volume -DriveLetter $candidate -ErrorAction SilentlyContinue
    if ($null -eq $taken) {{ $letter = $candidate; break }}
}}
if (-not $letter) {{ Write-Output 'SKIP: tidak ada drive letter tersedia.'; exit 0 }}

Resize-Partition -DriveLetter C -Size $targetSize | Out-Null
New-Partition -DiskNumber $disk.Number -UseMaximumSize -DriveLetter $letter | Out-Null
Format-Volume -DriveLetter $letter -FileSystem NTFS -NewFileSystemLabel 'Data' -Confirm:$false | Out-Null
Write-Output ""OK: partisi Data $dataGB GB dibuat sebagai drive $letter`:`""
";
    }

    // -------------------------------------------------------------- drivers

    public QuickDriverPlan GetDriverPlan()
    {
        var manufacturer = QueryScalar("SELECT Manufacturer FROM Win32_ComputerSystem");

        if (manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickDriverPlan
            {
                Brand = "Lenovo",
                Description = "Install Lenovo System Update (tools\\lenovo\\LenovoUpdate.exe)",
                UsesLocalInstaller = true,
                NeedsWindowsUpdateConfirm = false
            };
        }

        if (manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickDriverPlan
            {
                Brand = "Dell",
                Description = "Install .NET SDK lalu Dell Command Update (tools\\dell)",
                UsesLocalInstaller = true,
                NeedsWindowsUpdateConfirm = false
            };
        }

        return new QuickDriverPlan
        {
            Brand = "Other",
            Description = $"Brand {manufacturer} memakai Windows Update (driver-only)",
            UsesLocalInstaller = false,
            NeedsWindowsUpdateConfirm = true
        };
    }

    public Task<(bool Success, string Message)> InstallVendorDriverAsync()
    {
        return Task.Run(async () =>
        {
            var plan = GetDriverPlan();
            var toolsRoot = Path.Combine(_workspaceRoot, "tools");

            if (plan.Brand == "Lenovo")
            {
                var lenovo = Path.Combine(toolsRoot, "lenovo", "LenovoUpdate.exe");
                if (!File.Exists(lenovo))
                {
                    return (false, $"File tidak ditemukan: {lenovo}");
                }

                Log($"Menjalankan Lenovo System Update: {lenovo}");
                var result = await RunProcessAsync(lenovo, "/VERYSILENT /NORESTART /SP-", TimeSpan.FromMinutes(30));
                var ok = result.ExitCode is 0 or 1641 or 3010;
                return (ok, $"Lenovo System Update exit={result.ExitCode}");
            }

            if (plan.Brand == "Dell")
            {
                var dellDir = Path.Combine(toolsRoot, "dell");

                var sdk = Directory.EnumerateFiles(dellDir, "dotnet-sdk-*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f).FirstOrDefault();
                if (sdk is null)
                {
                    return (false, "dotnet-sdk tidak ditemukan di tools\\dell.");
                }

                Log($"Menjalankan .NET SDK: {Path.GetFileName(sdk)} (1/2)");
                var sdkResult = await RunProcessAsync(sdk, "/install /quiet /norestart", TimeSpan.FromMinutes(25));
                if (sdkResult.ExitCode is not (0 or 1641 or 3010))
                {
                    return (false, $".NET SDK gagal exit={sdkResult.ExitCode}");
                }

                var dcu = Directory.EnumerateFiles(dellDir, "Dell-Command-Update-*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f).FirstOrDefault();
                if (dcu is null)
                {
                    return (false, "Dell Command Update tidak ditemukan di tools\\dell.");
                }

                Log($"Menjalankan Dell Command Update: {Path.GetFileName(dcu)} (2/2)");
                var dcuResult = await RunProcessAsync(dcu, "/S", TimeSpan.FromMinutes(35));
                var ok = dcuResult.ExitCode is 0 or 1641 or 3010;
                return (ok, $"Dell Command Update exit={dcuResult.ExitCode}");
            }

            return (false, "Brand ini tidak memakai installer lokal.");
        });
    }

    public void StartWindowsUpdateDriverOnly()
    {
        var script = @"
$ErrorActionPreference = 'Continue'
Write-Output 'Mulai Windows Update driver-only...'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search(""IsInstalled=0 and Type='Driver'"")
Write-Output ('Driver ditemukan: ' + $result.Updates.Count)
if ($result.Updates.Count -eq 0) { Write-Output 'Tidak ada driver baru.'; exit 0 }
$collection = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($update in $result.Updates) { [void]$collection.Add($update) }
$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $collection
$downloader.Download()
$installer = $session.CreateUpdateInstaller()
$installer.Updates = $collection
$installResult = $installer.Install()
Write-Output ('Install selesai. ResultCode: ' + $installResult.ResultCode)
";

        var tempPath = Path.Combine(Path.GetTempPath(), $"ryota-wu-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tempPath, script);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Minimized
            });
            Log("Windows Update driver-only berjalan di background.", "INFO");
        }
        catch (Exception ex)
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
            Log($"Gagal menjalankan Windows Update: {ex.Message}", "ERROR");
        }
    }

    // ---------------------------------------------------------------- office

    public void LaunchOfficeSetup()
    {
        var setup = Path.Combine(_workspaceRoot, "tools", "addons", "Office", "Setup.exe");
        if (!File.Exists(setup))
        {
            Log($"Office Setup.exe tidak ditemukan: {setup}", "ERROR");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = setup,
                WorkingDirectory = Path.GetDirectoryName(setup),
                UseShellExecute = true,
                Verb = "runas"
            });
            Log("Installer Microsoft Office diluncurkan (berjalan di background).", "INFO");
        }
        catch (Exception ex)
        {
            Log($"Gagal meluncurkan Office: {ex.Message}", "ERROR");
        }
    }

    // ------------------------------------------------------------------ apps

    public Task<List<QuickAppResult>> InstallOfflineAppsAsync()
    {
        return Task.Run(async () =>
        {
            var results = new List<QuickAppResult>();
            var offlineDir = Path.Combine(_workspaceRoot, "tools", "addons", "offline");
            if (!Directory.Exists(offlineDir))
            {
                Log($"Folder offline tidak ditemukan: {offlineDir}", "ERROR");
                return results;
            }

            var installers = Directory.EnumerateFiles(offlineDir, "*.exe", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals("AppPack.exe", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log($"Ditemukan {installers.Count} installer offline.");

            foreach (var installer in installers)
            {
                var name = Path.GetFileNameWithoutExtension(installer);
                var args = GetSilentArgs(Path.GetFileName(installer));
                if (args is null)
                {
                    Log($"[SKIP] {name}: argumen silent tidak dikenal.", "WARN");
                    results.Add(new QuickAppResult { Name = name, Success = false, Detail = "Dilewati (argumen tidak dikenal)" });
                    continue;
                }

                Log($"Menginstal {name}...");
                var result = await RunProcessAsync(installer, args, TimeSpan.FromMinutes(20));
                var ok = result.ExitCode is 0 or 1641 or 3010 or 1638 or 3011;
                Log($"{name}: {(ok ? "berhasil" : $"gagal (exit={result.ExitCode})")}", ok ? "SUCCESS" : "ERROR");
                results.Add(new QuickAppResult { Name = name, Success = ok, Detail = ok ? "Terinstal" : $"Exit code {result.ExitCode}" });
            }

            return results;
        });
    }

    internal static string? GetSilentArgs(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        if (name.EndsWith(".msi"))
        {
            return null; // handled via msiexec separately if needed
        }

        if (name.StartsWith("dxsetup")) return "/silent";
        if (name.StartsWith("directx")) return "/Q";
        if (name.StartsWith("chrome")) return "/silent /install";
        if (name.StartsWith("firefox")) return "/S";
        if (name.StartsWith("teamviewer")) return "/S";
        if (name.Contains("vcredist") || name.Contains("visualcppredist")) return "/Y";
        if (name.StartsWith("vlc")) return "/S";
        if (name.StartsWith("winrar")) return "/S";
        if (name.StartsWith("zoom")) return "/silent";
        if (name.StartsWith("canva")) return "/S";
        return null;
    }

    public void LaunchOnlineApps()
    {
        var appPack = Path.Combine(_workspaceRoot, "tools", "addons", "online", "AppPack.exe");
        if (!File.Exists(appPack))
        {
            Log($"AppPack.exe tidak ditemukan: {appPack}", "ERROR");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = appPack,
                WorkingDirectory = Path.GetDirectoryName(appPack),
                UseShellExecute = true,
                Verb = "runas"
            });
            Log("AppPack (online) diluncurkan di background.", "INFO");
        }
        catch (Exception ex)
        {
            Log($"Gagal meluncurkan AppPack: {ex.Message}", "ERROR");
        }
    }

    // ------------------------------------------------------------- wallpaper

    public Task<List<string>> GetWallpaperFilesAsync()
    {
        return Task.Run(() =>
        {
            var dir = Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Wallpapers");
            if (!Directory.Exists(dir))
            {
                return new List<string>();
            }

            return Directory.EnumerateFiles(dir)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public bool SetWallpaper(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Log($"Wallpaper tidak ditemukan: {filePath}", "ERROR");
                return false;
            }

            var result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, filePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var ok = result != 0;
            Log(ok ? $"Wallpaper terpasang: {Path.GetFileName(filePath)}" : "Wallpaper mungkin tidak terpasang (API 0).", ok ? "SUCCESS" : "WARN");
            return ok;
        }
        catch (Exception ex)
        {
            Log($"Gagal set wallpaper: {ex.Message}", "ERROR");
            return false;
        }
    }

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    // ---------------------------------------------------------------- helpers

    private async Task<(bool Success, int ExitCode, string Output)> RunProcessAsync(
        string fileName, string arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(info);
        if (process is null)
        {
            return (false, -1, "Gagal memulai proses.");
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = false;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
            exited = true;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        if (!exited)
        {
            return (false, -1, "Timeout.");
        }

        var exitCode = process.ExitCode;
        return (exitCode == 0, exitCode, output.ToString());
    }
}
