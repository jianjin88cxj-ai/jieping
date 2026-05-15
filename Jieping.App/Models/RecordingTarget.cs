namespace Jieping.App.Models;

public sealed class RecordingTarget
{
    public RecordingTarget(RecordingMode mode, string description, CaptureRegion? region = null, nint? windowHandle = null)
    {
        Mode = mode;
        Description = description;
        Region = region;
        WindowHandle = windowHandle;
    }

    public RecordingMode Mode { get; }

    public string Description { get; }

    public CaptureRegion? Region { get; }

    public nint? WindowHandle { get; }
}
