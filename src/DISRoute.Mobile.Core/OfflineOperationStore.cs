using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Mobile.Core;

public sealed class OfflineOperationStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IOfflineDataProtector _protector;
    private List<QueuedMobileOperationDto> _items;

    public OfflineOperationStore(string path, IOfflineDataProtector protector)
    {
        _path = path;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _items = Load(path, protector);
    }

    public IReadOnlyList<QueuedMobileOperationDto> Items { get { lock (_gate) return _items.OrderBy(x => x.OccurredAtUtc).ToArray(); } }

    public QueuedMobileOperationDto Enqueue(string kind, Guid? incidentId, string payloadJson, DateTimeOffset? occurredAtUtc = null)
    {
        lock (_gate)
        {
            if (kind is not ("Message" or "Narrative" or "ChatMessage" or "UnitStatus")) throw new InvalidOperationException("Only explicitly offline-safe mobile operations may be queued.");
            var item = new QueuedMobileOperationDto(Guid.NewGuid(), kind, incidentId, payloadJson, occurredAtUtc ?? DateTimeOffset.UtcNow);
            _items.Add(item);
            Save();
            return item;
        }
    }

    public void ApplyResults(IEnumerable<MobileOperationResultDto> results)
    {
        lock (_gate)
        {
            var completed = results.Where(x => x.State is "Accepted" or "DuplicateIgnored").Select(x => x.OperationId).ToHashSet();
            _items.RemoveAll(x => completed.Contains(x.OperationId));
            Save();
        }
    }

    public bool Remove(Guid operationId)
    {
        lock (_gate)
        {
            var removed = _items.RemoveAll(x => x.OperationId == operationId) > 0;
            if (removed) Save();
            return removed;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        var json = JsonSerializer.Serialize(_items);
        var protectedData = _protector.Protect(Encoding.UTF8.GetBytes(json));
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedData);
        File.Move(temp, _path, true);
    }

    private static List<QueuedMobileOperationDto> Load(string path, IOfflineDataProtector protector)
    {
        if (!File.Exists(path)) return [];
        var protectedData = File.ReadAllBytes(path);
        var json = Encoding.UTF8.GetString(protector.Unprotect(protectedData));
        return JsonSerializer.Deserialize<List<QueuedMobileOperationDto>>(json) ?? [];
    }
}
