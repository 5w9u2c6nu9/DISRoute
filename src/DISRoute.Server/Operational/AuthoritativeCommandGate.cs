namespace DISRoute.Server.Operational;

/// <summary>
/// Serializes PostgreSQL authoritative command/checkpoint boundaries on a node. This is deliberately
/// correctness-first: optimistic concurrency still resolves competing dispatcher edits, while the
/// gate prevents a checkpoint from observing a half-completed API command.
/// </summary>
public sealed class AuthoritativeCommandGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
