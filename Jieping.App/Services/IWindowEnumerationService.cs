using Jieping.App.Models;

namespace Jieping.App.Services;

public interface IWindowEnumerationService
{
    IReadOnlyList<DesktopWindowInfo> GetVisibleWindows();

    DesktopWindowInfo ResolveWindow(nint handle);
}
