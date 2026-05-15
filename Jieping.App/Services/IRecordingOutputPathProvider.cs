namespace Jieping.App.Services;

public interface IRecordingOutputPathProvider
{
    string CreateDefaultPath(string outputDirectory, DateTimeOffset timestamp);
}
