namespace Jieping.App.Recording;

public sealed record VideoGifExportRequest(
    string InputPath,
    int FrameRate,
    int Width);
