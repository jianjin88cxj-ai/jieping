namespace Jieping.App.Recording;

public sealed class VideoRecorderException : Exception
{
    public VideoRecorderException(string message)
        : base(message)
    {
    }

    public VideoRecorderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
