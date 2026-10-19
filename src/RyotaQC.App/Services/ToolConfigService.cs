using System.IO;
using System.Text.Json;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class ToolConfigService : IToolConfigService
{
    private readonly IAppLogger _logger;
    private readonly string _workspaceRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private List<ToolConfig> _tools = [];

    public ToolConfigService(IAppLogger logger)
    {
        _logger = logger;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        ToolConfigPath = ResolveConfigPath();
    }

    public string ToolConfigPath { get; }

    public void Load()
    {
        if (!File.Exists(ToolConfigPath))
        {
            _logger.Error($"tool-map.json tidak ditemukan: {ToolConfigPath}");
            _tools = [];
            return;
        }

        try
        {
            var raw = File.ReadAllText(ToolConfigPath);
            var config = JsonSerializer.Deserialize<ToolMapConfig>(raw, _jsonOptions) ?? new ToolMapConfig();
            _tools = config.Tools
                .Where(tool => !string.IsNullOrWhiteSpace(tool.Id))
                .ToList();
            _logger.Info($"Loaded {_tools.Count} tool launcher entries.");
        }
        catch (Exception ex)
        {
            _tools = [];
            _logger.Error("Gagal parse tool-map.json", ex);
        }
    }

    public IReadOnlyList<ToolConfig> GetAllTools()
    {
        return _tools;
    }

    public ToolConfig? GetToolById(string id)
    {
        return _tools.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public string ResolvePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return value;
        }

        if (Path.IsPathRooted(value))
        {
            return value;
        }

        var normalized = value.Replace('/', Path.DirectorySeparatorChar);

        // Command-style values (cmd.exe, powershell.exe, diskmgmt.msc) can be launched via PATH/System.
        if (!normalized.Contains(Path.DirectorySeparatorChar) && !normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            return normalized;
        }

        return Path.Combine(_workspaceRoot, normalized);
    }

    public bool IsToolPathValid(ToolConfig tool)
    {
        if (tool.Type.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedUrl = ResolvePath(tool.RelativePath);
            return Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        var resolved = ResolvePath(tool.RelativePath);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        if (IsCommandStylePath(resolved))
        {
            return true;
        }

        return File.Exists(resolved) || Directory.Exists(resolved);
    }

    public bool IsSupportedType(string toolType)
    {
        if (string.IsNullOrWhiteSpace(toolType))
        {
            return false;
        }

        var normalized = toolType.Trim().ToLowerInvariant();
        return normalized is "exe" or "bat" or "cmd" or "powershell" or "msc" or "url";
    }

    private static bool IsCommandStylePath(string value)
    {
        return !value.Contains(Path.DirectorySeparatorChar) && !value.Contains(Path.AltDirectorySeparatorChar);
    }

    private string ResolveConfigPath()
    {
        var appBase = AppContext.BaseDirectory;
        var runtimePath = Path.Combine(appBase, "Config", "tool-map.json");
        if (File.Exists(runtimePath))
        {
            return runtimePath;
        }

        return Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Config", "tool-map.json");
    }
}
