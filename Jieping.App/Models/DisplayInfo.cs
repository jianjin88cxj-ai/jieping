namespace Jieping.App.Models;

public sealed record DisplayInfo(
    int Index,
    string DeviceName,
    bool IsPrimary,
    CaptureRegion Bounds)
{
    public string DisplayName
    {
        get
        {
            var primarySuffix = IsPrimary ? " (Primary)" : string.Empty;
            return $"Display {Index + 1}{primarySuffix}: {Bounds.Width} x {Bounds.Height} at ({Bounds.X}, {Bounds.Y})";
        }
    }
}
