using Jieping.App.Models;
using Forms = System.Windows.Forms;

namespace Jieping.App.Services;

public sealed class DisplayBoundsService : IDisplayBoundsService
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        return Forms.Screen.AllScreens
            .Select((screen, index) => new DisplayInfo(
                index,
                screen.DeviceName,
                screen.Primary,
                new CaptureRegion(
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    screen.Bounds.Width,
                    screen.Bounds.Height)))
            .ToList();
    }

    public CaptureRegion GetPrimaryDisplayBounds()
    {
        var primaryDisplay = GetDisplays().FirstOrDefault(display => display.IsPrimary)
            ?? throw new InvalidOperationException("Primary display bounds are not available.");

        return primaryDisplay.Bounds;
    }
}
