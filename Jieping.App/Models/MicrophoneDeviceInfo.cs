namespace Jieping.App.Models;

public sealed record MicrophoneDeviceInfo(int DeviceNumber, string Name)
{
    public string DisplayName => $"{DeviceNumber}: {Name}";
}
