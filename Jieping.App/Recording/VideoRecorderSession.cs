namespace Jieping.App.Recording;

public sealed record VideoRecorderSession(
    string OutputPath,
    int Width,
    int Height,
    int FrameRate,
    DateTimeOffset StartedAt);
