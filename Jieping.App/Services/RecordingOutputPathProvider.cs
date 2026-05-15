using System.IO;

namespace Jieping.App.Services;

public sealed class RecordingOutputPathProvider : IRecordingOutputPathProvider
{
    public string CreateDefaultPath(string outputDirectory, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fileName = $"Recording_{timestamp.LocalDateTime:yyyy-MM-dd_HHmmss}.mp4";
        return Path.Combine(outputDirectory, fileName);
    }
}
