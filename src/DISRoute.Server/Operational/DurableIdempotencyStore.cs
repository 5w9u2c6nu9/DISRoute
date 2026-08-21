using System.Security.Cryptography;
using System.Text.Json;

namespace DISRoute.Server.Operational;

public sealed record IdempotencyReceiptState(
    string Key,
    string Operation,
    string RequestSha256,
    string ResultType,
    string ResultJson,
    DateTimeOffset CommittedAtUtc);

/// <summary>
/// Durable command-retry ledger shared by all authoritative stores. A key is bound to one
/// operation and one request payload; reusing it for anything else is rejected instead of
/// silently replaying an unrelated result. Receipts are checkpointed and projected to PostgreSQL.
/// </summary>
public sealed class DurableIdempotencyStore
{
    private const int MaxKeyLength = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, IdempotencyReceiptState> _receipts = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        WriteIndented = false
    };

    public int Count { get { lock (_gate) return _receipts.Count; } }

    public bool TryGet<T>(string? key, string operation, object? request, out T value)
    {
        var normalized = NormalizeKey(key);
        if (normalized is null)
        {
            value = default!;
            return false;
        }

        var requestSha256 = Fingerprint(request);
        var resultType = TypeToken<T>();
        lock (_gate)
        {
            if (!_receipts.TryGetValue(normalized, out var receipt))
            {
                value = default!;
                return false;
            }

            EnsureCompatible(receipt, operation, requestSha256, resultType);
            value = JsonSerializer.Deserialize<T>(receipt.ResultJson, JsonOptions)
                ?? throw new InvalidDataException($"Idempotency receipt '{normalized}' contains an empty or incompatible result.");
            return true;
        }
    }

    public void Remember<T>(string? key, string operation, object? request, T value) where T : notnull
    {
        var normalized = NormalizeKey(key);
        if (normalized is null) return;

        var requestSha256 = Fingerprint(request);
        var resultType = TypeToken<T>();
        var resultJson = JsonSerializer.Serialize(value, JsonOptions);
        lock (_gate)
        {
            if (_receipts.TryGetValue(normalized, out var existing))
            {
                EnsureCompatible(existing, operation, requestSha256, resultType);
                return;
            }

            _receipts[normalized] = new IdempotencyReceiptState(
                normalized, operation, requestSha256, resultType, resultJson, DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<IdempotencyReceiptState> ExportState()
    {
        lock (_gate)
            return _receipts.Values.OrderBy(x => x.CommittedAtUtc).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
    }

    public void ImportState(IEnumerable<IdempotencyReceiptState>? state)
    {
        lock (_gate)
        {
            _receipts.Clear();
            foreach (var receipt in state ?? [])
            {
                var normalized = NormalizeKey(receipt.Key)
                    ?? throw new InvalidDataException("Persisted idempotency receipt has an empty key.");
                if (string.IsNullOrWhiteSpace(receipt.Operation) || string.IsNullOrWhiteSpace(receipt.RequestSha256) || string.IsNullOrWhiteSpace(receipt.ResultType))
                    throw new InvalidDataException($"Persisted idempotency receipt '{normalized}' is incomplete.");
                _receipts[normalized] = receipt with { Key = normalized };
            }
        }
    }

    private static void EnsureCompatible(IdempotencyReceiptState receipt, string operation, string requestSha256, string resultType)
    {
        if (!string.Equals(receipt.Operation, operation, StringComparison.Ordinal) ||
            !string.Equals(receipt.RequestSha256, requestSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.ResultType, resultType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Idempotency key '{receipt.Key}' has already been committed for a different command or payload. Use a new idempotency key.");
        }
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = key.Trim();
        if (normalized.Length > MaxKeyLength)
            throw new ArgumentException($"Idempotency key cannot exceed {MaxKeyLength} characters.", nameof(key));
        return normalized;
    }

    private static string Fingerprint(object? request)
    {
        var bytes = request is null
            ? "null"u8.ToArray()
            : JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string TypeToken<T>() => typeof(T).FullName ?? typeof(T).Name;
}
