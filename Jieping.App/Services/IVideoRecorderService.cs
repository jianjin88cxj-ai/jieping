using Jieping.App.Recording;

namespace Jieping.App.Services;

public interface IVideoRecorderService
{
    bool IsRecording { get; }

    Task<VideoRecorderSession> StartAsync(
        VideoRecorderStartRequest request,
        CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);

    Task<string> StopAsync(CancellationToken cancellationToken = default);
}
