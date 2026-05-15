namespace Jieping.App.Models;

public sealed record VideoBitrateOption(string DisplayName, int? KilobitsPerSecond)
{
    public static VideoBitrateOption Auto { get; } = new("Auto", null);
}
