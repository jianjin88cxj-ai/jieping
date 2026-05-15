namespace Jieping.App.Models;

public sealed record DesktopWindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    CaptureRegion Bounds)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName)
        ? Title
        : $"{Title} ({ProcessName})";
}
