using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface IToolConfigService
{
    void Load();

    IReadOnlyList<ToolConfig> GetAllTools();

    ToolConfig? GetToolById(string id);

    string ResolvePath(string value);

    bool IsToolPathValid(ToolConfig tool);

    bool IsSupportedType(string toolType);

    string ToolConfigPath { get; }
}
