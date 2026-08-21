using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Mobile.Core;

public sealed record MobileCachedSnapshotDto(MobileSyncResponseDto Response, DateTimeOffset CachedAtUtc);

public sealed class MobileCacheStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IOfflineDataProtector _protector;

    public MobileCacheStore(string path, IOfflineDataProtector protector)
    {
        _path = path;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public MobileCachedSnapshotDto? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            var protectedData = File.ReadAllBytes(_path);
            var json = Encoding.UTF8.GetString(_protector.Unprotect(protectedData));
            return JsonSerializer.Deserialize<MobileCachedSnapshotDto>(json);
        }
    }

    public void Save(MobileSyncResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var json = JsonSerializer.Serialize(new MobileCachedSnapshotDto(response, DateTimeOffset.UtcNow));
            var protectedData = _protector.Protect(Encoding.UTF8.GetBytes(json));
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, protectedData);
            File.Move(temp, _path, true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
