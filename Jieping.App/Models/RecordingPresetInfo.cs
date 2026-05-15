namespace Jieping.App.Models;

public sealed record RecordingPresetInfo(
    string Name,
    int FrameRate,
    string Quality,
    int? VideoBitrateKbps = null,
    bool IsCustom = false)
{
    public string DisplayName => IsCustom
        ? "Custom"
        : $"{Name}: {FrameRate} FPS, {Quality}, {FormatBitrate(VideoBitrateKbps)}";

    private static string FormatBitrate(int? videoBitrateKbps)
    {
        return videoBitrateKbps is { } bitrate
            ? $"{bitrate / 1000} Mbps"
            : "Auto bitrate";
    }
}
