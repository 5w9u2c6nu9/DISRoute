namespace DISRoute.Integrations;

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IProvider> Providers => _providers.Values;

    public void Register(IProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Key)) throw new ArgumentException("Provider key is required.", nameof(provider));
        if (!_providers.TryAdd(provider.Key, provider)) throw new InvalidOperationException($"Provider '{provider.Key}' is already registered.");
    }

    public T? Get<T>(string key) where T : class, IProvider => _providers.TryGetValue(key, out var provider) ? provider as T : null;

    public IReadOnlyList<T> GetAll<T>() where T : class, IProvider => _providers.Values.OfType<T>().ToArray();
}
