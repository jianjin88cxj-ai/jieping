using Jieping.App.Models;

namespace Jieping.App.Services;

public interface IDisplayBoundsService
{
    IReadOnlyList<DisplayInfo> GetDisplays();

    CaptureRegion GetPrimaryDisplayBounds();
}
