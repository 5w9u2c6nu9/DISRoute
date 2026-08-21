namespace DISRoute.Server.Operational;

/// <summary>Ambient correlation for mutations produced by one authoritative HTTP command.</summary>
internal static class DurabilityCommandContext
{
    private static readonly AsyncLocal<Guid?> Current = new();
    public static Guid? CurrentBatchId => Current.Value;

    public static IDisposable Begin(Guid batchId)
    {
        var prior = Current.Value;
        Current.Value = batchId;
        return new Scope(prior);
    }

    private sealed class Scope(Guid? prior) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current.Value = prior;
        }
    }
}
