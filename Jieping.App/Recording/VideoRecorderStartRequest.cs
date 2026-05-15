using Jieping.App.Models;

namespace Jieping.App.Recording;

public sealed record VideoRecorderStartRequest(
    RecordingTarget Target,
    string OutputDirectory,
    int FrameRate,
    string Quality,
    int? VideoBitrateKbps = null,
    bool CaptureCursor = true,
    bool HighlightMouseClicks = true,
    bool CaptureSystemAudio = false,
    bool CaptureMicrophoneAudio = false,
    int? MicrophoneDeviceNumber = null,
    bool IncludeWatermark = false,
    string? WatermarkText = null)
{
    public int Width => NormalizeDimension(Target.Region?.Width ?? 1280);

    public int Height => NormalizeDimension(Target.Region?.Height ?? 720);

    public bool UseTargetVideoBitrate => VideoBitrateKbps is > 0;

    private static int NormalizeDimension(int value)
    {
        var bounded = Math.Max(2, value);
        return bounded % 2 == 0 ? bounded : bounded - 1;
    }
}
