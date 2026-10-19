using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class WallpaperService : IWallpaperService
{
    private const int SpiSetDesktopWallpaper = 20;
    private const int SpiGetDesktopWallpaper = 0x0073;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendWinIniChange = 0x02;

    private readonly IAppLogger _logger;
    private readonly string _workspaceRoot;
    private readonly string _wallpaperConfigPath;
    private readonly string _runtimeWallpaperFolder;
    private readonly string _sourceWallpaperFolder;

    private List<WallpaperItemModel> _wallpapers = [];

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, StringBuilder pvParam, int fWinIni);

    public WallpaperService(IAppLogger logger)
    {
        _logger = logger;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        _wallpaperConfigPath = ResolveWallpaperConfigPath();
        _runtimeWallpaperFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Wallpapers");
        _sourceWallpaperFolder = Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Wallpapers");
        LoadInternal();
    }

    public IReadOnlyList<WallpaperItemModel> GetWallpapers()
    {
        LoadInternal();
        return _wallpapers;
    }

    public string GetCurrentWallpaperPath()
    {
        var builder = new StringBuilder(260);
        var success = SystemParametersInfo(SpiGetDesktopWallpaper, builder.Capacity, builder, 0);
        if (!success)
        {
            return string.Empty;
        }

        return builder.ToString();
    }

    public bool SetWallpaper(WallpaperItemModel wallpaper, out string error)
    {
        if (!File.Exists(wallpaper.AbsolutePath))
        {
            error = "File wallpaper tidak ditemukan.";
            return false;
        }

        var success = SystemParametersInfo(
            SpiSetDesktopWallpaper,
            0,
            wallpaper.AbsolutePath,
            SpifUpdateIniFile | SpifSendWinIniChange);

        if (!success)
        {
            error = "Gagal mengubah wallpaper desktop.";
            _logger.Error(error);
            return false;
        }

        error = string.Empty;
        _logger.Info($"Wallpaper changed: {wallpaper.AbsolutePath}");
        return true;
    }

    public void Refresh()
    {
        LoadInternal();
    }

    private void LoadInternal()
    {
        try
        {
            // Deduplicate by file name so source/runtime copies are not shown twice.
            var uniqueWallpapers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddWallpaperFromDirectory(_runtimeWallpaperFolder, uniqueWallpapers, overrideIfExists: true);
            AddWallpaperFromDirectory(_sourceWallpaperFolder, uniqueWallpapers, overrideIfExists: false);

            foreach (var configuredEntry in ReadConfiguredEntries())
            {
                var resolved = ResolvePath(configuredEntry);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                if (Directory.Exists(resolved))
                {
                    AddWallpaperFromDirectory(resolved, uniqueWallpapers, overrideIfExists: false);
                    continue;
                }

                if (File.Exists(resolved) && IsSupportedImageFile(resolved))
                {
                    AddWallpaperPath(Path.GetFullPath(resolved), uniqueWallpapers, overrideIfExists: false);
                }
            }

            _wallpapers = uniqueWallpapers.Values
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(path =>
            {
                return new WallpaperItemModel
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    RelativePath = Path.GetFileName(path),
                    AbsolutePath = path,
                    Exists = true
                };
            })
            .ToList();

            _logger.Info($"Loaded {_wallpapers.Count} wallpaper entries.");
        }
        catch (Exception ex)
        {
            _wallpapers = [];
            _logger.Error("Gagal memuat wallpaper config", ex);
        }
    }

    private string ResolveWallpaperConfigPath()
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "Config", "wallpaper-map.json");
        if (File.Exists(runtimePath))
        {
            return runtimePath;
        }

        return Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Config", "wallpaper-map.json");
    }

    private IEnumerable<string> ReadConfiguredEntries()
    {
        if (!File.Exists(_wallpaperConfigPath))
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            var raw = File.ReadAllText(_wallpaperConfigPath);
            var config = JsonSerializer.Deserialize<WallpaperMapConfig>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new WallpaperMapConfig();

            return config.Wallpapers
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal membaca wallpaper-map.json", ex);
            return Enumerable.Empty<string>();
        }
    }

    private static bool IsSupportedImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return SupportedImageExtensions.Contains(ext);
    }

    private static void AddWallpaperFromDirectory(string directoryPath, IDictionary<string, string> target, bool overrideIfExists)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories))
        {
            if (!IsSupportedImageFile(file))
            {
                continue;
            }

            AddWallpaperPath(Path.GetFullPath(file), target, overrideIfExists);
        }
    }

    private static void AddWallpaperPath(string fullPath, IDictionary<string, string> target, bool overrideIfExists)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        if (!target.ContainsKey(fileName) || overrideIfExists)
        {
            target[fileName] = fullPath;
        }
    }

    private string ResolvePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(value))
        {
            return value;
        }

        var normalized = value.Replace('/', Path.DirectorySeparatorChar);

        // Use output folder first for copied assets.
        var runtimeCandidate = Path.Combine(AppContext.BaseDirectory, normalized);
        if (File.Exists(runtimeCandidate))
        {
            return runtimeCandidate;
        }

        return Path.Combine(_workspaceRoot, "src", "RyotaQC.App", normalized);
    }
}

