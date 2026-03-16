namespace MicroClaw.Providers;

public sealed record ProviderInfo(string Name, string ModelId, string ServiceKey);

public sealed class ProviderRegistry
{
    private readonly List<ProviderInfo> _providers = [];

    public void Register(ProviderInfo info) => _providers.Add(info);

    public IReadOnlyList<ProviderInfo> All => _providers;
}
