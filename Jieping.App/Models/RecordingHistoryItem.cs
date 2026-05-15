using System.IO;

namespace Jieping.App.Models;

public sealed class RecordingHistoryItem
{
    public RecordingHistoryItem(string path, string artifactType, DateTimeOffset createdAt, string details)
    {
        Path = path;
        ArtifactType = artifactType;
        CreatedAt = createdAt;
        Details = details;
    }

    public string Path { get; }

    public string ArtifactType { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Details { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public bool Exists => File.Exists(Path);
}
