namespace Jieping.App.Recording;

public sealed record RecordingPerformanceSnapshot(
    int TargetFrameRate,
    double ActualCaptureFrameRate,
    double OutputFrameRate,
    int DuplicateFrames);
