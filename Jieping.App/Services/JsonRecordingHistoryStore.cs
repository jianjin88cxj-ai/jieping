using System.IO;
using System.Text.Json;
using Jieping.App.Models;

namespace Jieping.App.Services;

public sealed class JsonRecordingHistoryStore : IRecordingHistoryStore
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jieping",
        "recording-history.json");

    public IReadOnlyList<RecordingHistoryItem> Load(int maxItems)
    {
        if (!File.Exists(StorePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(StorePath);
            var document = JsonSerializer.Deserialize<RecordingHistoryDocument>(json, _serializerOptions);
            return (document?.Items ?? [])
                .Where(record => !string.IsNullOrWhiteSpace(record.Path))
                .OrderByDescending(record => record.CreatedAt)
                .Take(maxItems)
                .Select(record => new RecordingHistoryItem(
                    record.Path!,
                    string.IsNullOrWhiteSpace(record.ArtifactType) ? "File" : record.ArtifactType!,
                    record.CreatedAt,
                    record.Details ?? string.Empty))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<RecordingHistoryItem> items, int maxItems)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var document = new RecordingHistoryDocument
        {
            SchemaVersion = 1,
            Items = items
                .Take(maxItems)
                .Select(item => new RecordingHistoryRecord
                {
                    Path = item.Path,
                    ArtifactType = item.RawArtifactType,
                    CreatedAt = item.CreatedAt,
                    Details = item.RawDetails
                })
                .ToList()
        };
        var tempPath = $"{StorePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, _serializerOptions));
            File.Move(tempPath, StorePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(StorePath))
        {
            File.Delete(StorePath);
        }
    }

    private sealed class RecordingHistoryRecord
    {
        public string? Path { get; set; }

        public string? ArtifactType { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string? Details { get; set; }
    }

    private sealed class RecordingHistoryDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public List<RecordingHistoryRecord> Items { get; set; } = [];
    }
}
