using Jieping.App.Models;

namespace Jieping.App.Services;

public interface IUpdatePackageDownloadService
{
    string DownloadDirectory { get; }

    Task<UpdatePackageDownloadResult> DownloadAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default);
}
