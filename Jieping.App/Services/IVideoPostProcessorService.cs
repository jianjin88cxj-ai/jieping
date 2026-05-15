using Jieping.App.Recording;

namespace Jieping.App.Services;

public interface IVideoPostProcessorService
{
    Task<string> TrimAsync(VideoTrimRequest request, CancellationToken cancellationToken = default);

    Task<string> ExportGifAsync(VideoGifExportRequest request, CancellationToken cancellationToken = default);
}
