using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface IExternalToolLauncher
{
    ToolLaunchResult LaunchById(string id);

    ToolLaunchResult Launch(ToolConfig tool);
}
