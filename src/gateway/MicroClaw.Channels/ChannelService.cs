using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.WeCom;
using MicroClaw.Channels.WeChat;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels;

public sealed class ChannelService : IChannelService
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly IReadOnlyDictionary<ChannelType, IChannelProvider> _providers;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    // ── IService ─────────────────────────────────────────────────────────

    public int InitOrder => 10;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        EnsureWebChannel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ChannelService(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        // Build providers internally — no longer injected from DI
        var providers = new Dictionary<ChannelType, IChannelProvider>
        {
            [ChannelType.Web] = new WebChannelProvider(serviceProvider.GetRequiredService<IMicroHubService>()),
            [ChannelType.WeCom] = new WeComChannelProvider(),
            [ChannelType.WeChat] = new WeChatChannelProvider(),
            [ChannelType.Feishu] = new FeishuChannelProvider(this, serviceProvider, loggerFactory),
        };
        _providers = providers;
    }

    // ── Config CRUD ───────────────────────────────────────────────────────

    public IReadOnlyList<ChannelEntity> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return [.. MicroClawConfig.Get<ChannelOptions>().Channels.Select(WithResolvedEnvVars)]; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public ChannelEntity? GetById(string id)
    {
        _lock.EnterReadLock();
        try
        {
            var e = MicroClawConfig.Get<ChannelOptions>().Channels.FirstOrDefault(c => c.Id == id);
            return e is null ? null : WithResolvedEnvVars(e);
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<ChannelEntity> GetConfigsByType(ChannelType type)
    {
        _lock.EnterReadLock();
        try
        {
            return [.. MicroClawConfig.Get<ChannelOptions>().Channels
                .Where(c => c.ChannelType == type)
                .Select(WithResolvedEnvVars)];
        }
        finally { _lock.ExitReadLock(); }
    }

    public ChannelEntity Add(ChannelEntity channel)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var withId = new ChannelEntity
            {
                Id          = MicroClawUtils.GetUniqueId(),
                DisplayName = channel.DisplayName,
                ChannelType = channel.ChannelType,
                IsEnabled   = channel.IsEnabled,
                SettingJson = channel.SettingJson,
            };
            MicroClawConfig.Save(new ChannelOptions { Channels = [.. opts.Channels, withId] });
            return withId;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public ChannelEntity? Update(string id, ChannelEntity incoming)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var existing = opts.Channels.FirstOrDefault(c => c.Id == id);
            if (existing is null) return null;

            var merged = new ChannelEntity
            {
                Id          = id,
                DisplayName = incoming.DisplayName,
                ChannelType = incoming.ChannelType,
                IsEnabled   = incoming.IsEnabled,
                SettingJson = MergeSettings(existing.SettingJson, incoming.SettingJson, incoming.ChannelType),
            };
            var updatedList = opts.Channels.Select(c => c.Id == id ? merged : c).ToList();
            MicroClawConfig.Save(new ChannelOptions { Channels = updatedList });
            return merged;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Delete(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var newList = opts.Channels.Where(c => c.Id != id).ToList();
            if (newList.Count == opts.Channels.Count) return false;
            MicroClawConfig.Save(new ChannelOptions { Channels = newList });
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>幂等创建内置 Web Channel。系统启动时调用，若已存在则跳过。</summary>
    public void EnsureWebChannel()
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            if (opts.Channels.Any(c => c.Id == WebChannelId)) return;
            MicroClawConfig.Save(new ChannelOptions
            {
                Channels = [.. opts.Channels, new ChannelEntity
                {
                    Id          = WebChannelId,
                    DisplayName = "Web Console",
                    ChannelType = ChannelType.Web,
                    IsEnabled   = true,
                    SettingJson = "{}",
                }]
            });
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── IChannelService ───────────────────────────────────────────────────

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

        ChannelEntity? config = GetById(channelId);
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
        return GetConfigsByType(type)
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

    // ── Static Helpers ────────────────────────────────────────────────────

    /// <summary>内置 Web Channel 的固定 ID。</summary>
    public const string WebChannelId = "web";

    public static ChannelType ParseChannelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "web"    => ChannelType.Web,
            "feishu" => ChannelType.Feishu,
            "wecom"  => ChannelType.WeCom,
            "wechat" => ChannelType.WeChat,
            _        => ChannelType.Web
        };

    public static string SerializeChannelType(ChannelType type) =>
        type switch
        {
            ChannelType.Web    => "web",
            ChannelType.Feishu => "feishu",
            ChannelType.WeCom  => "wecom",
            ChannelType.WeChat => "wechat",
            _                  => "web"
        };

    public static string MaskSettingsSecrets(string? settingsJson, ChannelType type)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return "{}";

        if (type == ChannelType.Feishu)
        {
            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(settingsJson);
            if (settings is null) return "{}";

            return JsonSerializer.Serialize(settings with
            {
                AppSecret         = MaskSecret(settings.AppSecret),
                EncryptKey        = MaskSecret(settings.EncryptKey),
                VerificationToken = MaskSecret(settings.VerificationToken),
            });
        }

        return settingsJson;
    }

    internal static string MaskSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (secret.Length <= 8) return "***";
        return secret[..4] + "***" + secret[^4..];
    }

    // ── Private Helpers ───────────────────────────────────────────────────

    private static string MergeSettings(string existingJson, string incomingJson, ChannelType type)
    {
        if (type != ChannelType.Feishu) return incomingJson;

        FeishuChannelSettings? existing = FeishuChannelSettings.TryParse(existingJson);
        FeishuChannelSettings? incoming = FeishuChannelSettings.TryParse(incomingJson);
        if (existing is null || incoming is null) return incomingJson;

        string appSecret         = IsMasked(incoming.AppSecret)         ? existing.AppSecret         : incoming.AppSecret;
        string encryptKey        = IsMasked(incoming.EncryptKey)        ? existing.EncryptKey        : incoming.EncryptKey;
        string verificationToken = IsMasked(incoming.VerificationToken) ? existing.VerificationToken : incoming.VerificationToken;

        return JsonSerializer.Serialize(incoming with
        {
            AppSecret         = appSecret,
            EncryptKey        = encryptKey,
            VerificationToken = verificationToken,
        });
    }

    private static bool IsMasked(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("***");

    private static ChannelEntity WithResolvedEnvVars(ChannelEntity e) =>
        new()
        {
            Id          = e.Id,
            DisplayName = e.DisplayName,
            ChannelType = e.ChannelType,
            IsEnabled   = e.IsEnabled,
            SettingJson = ResolveEnvVars(e.SettingJson) ?? "{}",
        };

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }

    private static string BuildFingerprint(ChannelEntity config)
        => string.Join("|", config.Id, config.DisplayName, config.ChannelType, config.IsEnabled, config.SettingJson);

    private sealed record CacheEntry(string Fingerprint, IChannel Channel);
}
