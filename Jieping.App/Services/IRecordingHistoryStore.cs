using Jieping.App.Models;

namespace Jieping.App.Services;

public interface IRecordingHistoryStore
{
    IReadOnlyList<RecordingHistoryItem> Load(int maxItems);

    void Save(IEnumerable<RecordingHistoryItem> items, int maxItems);

    void Clear();
}
