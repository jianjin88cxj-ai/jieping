using System.IO;

namespace Jieping.App.Models;

public sealed class AppConfiguration
{
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "Jieping");

    public int FrameRate { get; set; } = 30;

    public string Quality { get; set; } = "Standard";

    public string RecordingPreset { get; set; } = "Balanced";

    public string LanguageCode { get; set; } = "en-US";

    public int? VideoBitrateKbps { get; set; }

    public bool CaptureCursor { get; set; } = true;

    public bool HighlightMouseClicks { get; set; } = true;

    public bool IncludeWatermark { get; set; }

    public string WatermarkText { get; set; } = "Jieping";

    public string UpdateManifestLocation { get; set; } = string.Empty;

    public bool AllowCrashReports { get; set; }

    public bool CaptureSystemAudio { get; set; }

    public bool CaptureMicrophoneAudio { get; set; }

    public int? MicrophoneDeviceNumber { get; set; }
}
