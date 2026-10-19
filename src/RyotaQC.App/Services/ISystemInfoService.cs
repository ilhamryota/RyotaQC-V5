using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface ISystemInfoService
{
    Task<SystemInfoModel> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}
