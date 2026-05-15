using Jieping.App.Models;

namespace Jieping.App.Services;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(
        string manifestLocation,
        Version currentVersion,
        CancellationToken cancellationToken = default);
}
