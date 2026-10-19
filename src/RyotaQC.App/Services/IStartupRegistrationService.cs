namespace RyotaQC.App.Services;

public interface IStartupRegistrationService
{
    void EnsureStartupEnabled();

    void DisableStartup();
}
