namespace RyotaQC.App.Models;

public sealed class ToolMapConfig
{
    public List<ToolConfig> Tools { get; init; } = [];
}

public sealed class ToolConfig
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public bool RunAsAdmin { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// When true, the launched console window stays open after the command finishes.
    /// </summary>
    public bool KeepOpen { get; init; }
}

public sealed class ToolLaunchResult
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public string ToolId { get; init; } = string.Empty;
}
