using System.Collections.Concurrent;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels;

public sealed class ChannelService(
    ChannelConfigStore configStore,
    IEnumerable<IChannelProvider> providers) : IChannelService
{
    private readonly ChannelConfigStore _configStore = configStore;
    private readonly IReadOnlyDictionary<ChannelType, IChannelProvider> _providers =
        providers
            .GroupBy(static provider => provider.Type)
            .ToDictionary(static group => group.Key, static group => group.Last());

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public IChannel GetRequired(string channelId)
    {
        if (!TryGet(channelId, out IChannel? channel))
            throw new InvalidOperationException($"No channel is registered for id '{channelId}'.");

        return channel;
    }

    public bool TryGet(string channelId, out IChannel? channel)
    {
        channel = null;
        if (string.IsNullOrWhiteSpace(channelId))
            return false;

        ChannelEntity? config = _configStore.GetById(channelId);
        if (config is null)
        {
            _cache.TryRemove(channelId, out _);
            return false;
        }

        IChannelProvider provider = GetRequiredProvider(config.ChannelType);
        string fingerprint = BuildFingerprint(config);

        CacheEntry entry = _cache.AddOrUpdate(
            channelId,
            _ => new CacheEntry(fingerprint, provider.Create(config)),
            (_, existing) => existing.Fingerprint == fingerprint
                ? existing
                : new CacheEntry(fingerprint, provider.Create(config)));

        channel = entry.Channel;
        return true;
    }

    public IReadOnlyList<IChannel> GetByType(ChannelType type)
    {
        return _configStore.GetByType(type)
            .Select(config => GetRequired(config.Id))
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<IChannelProvider> GetProviders()
        => _providers.Values.OrderBy(static provider => provider.Type).ToList().AsReadOnly();

    public IChannelProvider GetRequiredProvider(ChannelType type)
    {
        if (_providers.TryGetValue(type, out IChannelProvider? provider))
            return provider;

        throw new InvalidOperationException($"No channel provider is registered for type '{type}'.");
    }

    private static string BuildFingerprint(ChannelEntity config)
        => string.Join("|", config.Id, config.DisplayName, config.ChannelType, config.IsEnabled, config.SettingJson);

    private sealed record CacheEntry(string Fingerprint, IChannel Channel);
}
