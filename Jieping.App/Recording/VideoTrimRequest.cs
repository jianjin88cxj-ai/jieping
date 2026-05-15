namespace Jieping.App.Recording;

public sealed record VideoTrimRequest(
    string InputPath,
    double TrimStartSeconds,
    double TrimEndSeconds);
