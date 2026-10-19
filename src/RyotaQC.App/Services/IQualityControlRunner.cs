namespace RyotaQC.App.Services;

public interface IQualityControlRunner
{
    void RunLcdTest();

    void RunSentinel();

    void RunBatteryHealth();

    void RunKeyboardTest();

    void RunCameraTest();

    void RunAudioTest();

    void RunRunningTest();

    Task RunBatteryReportAsync();

    bool RunTouchscreenTest();

    TouchpadTestResult RunTouchpadTest();

    bool RunMicTest();

    void RunExternalTarget(string fullPath, bool runAsAdmin = false, string? displayName = null);
}

public readonly record struct TouchpadTestResult(
    bool HasMovement,
    bool HasLeftClick,
    bool HasRightClick,
    bool HasScroll,
    double DistancePx,
    int LeftClicks,
    int RightClicks,
    int ScrollSteps)
{
    public bool Passed => HasMovement && HasLeftClick && HasRightClick && HasScroll;

    public string ToInfoText()
    {
        return $"Move={DistancePx:0} px, Left={LeftClicks}, Right={RightClicks}, Scroll={ScrollSteps}";
    }
}
