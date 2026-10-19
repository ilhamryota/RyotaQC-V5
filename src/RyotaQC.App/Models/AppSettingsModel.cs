namespace RyotaQC.App.Models;

public sealed class AppSettingsModel
{
    public string AppTitle { get; init; } = "RyotaQC V5";

    public string AccentColor { get; init; } = "#39C8FF";

    public string TileBackgroundImagePath { get; init; } = "Assets/Images/Tiles";

    public string DefaultToolsFolder { get; init; } = "tools";

    public string DefaultQcFolder { get; init; } = "tools/qc";

    public bool LaunchOnStartup { get; set; } = true;

    public bool LightweightMode { get; init; } = true;

    public int SplashDelayMs { get; init; } = 300;

    public bool EnablePageTransition { get; init; } = true;

    public int PageTransitionMs { get; init; } = 120;

    public WindowSettings Window { get; init; } = new();
}

public sealed class WindowSettings
{
    public double Width { get; init; } = 1280;

    public double Height { get; init; } = 800;
}
