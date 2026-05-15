namespace Jieping.App.Models;

public enum RecordingState
{
    Idle,
    TargetSelected,
    Countdown,
    Recording,
    Paused,
    Stopping,
    Completed,
    Error
}
