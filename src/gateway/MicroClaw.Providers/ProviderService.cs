using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers.Claude;
using MicroClaw.Providers.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Providers;

/// <summary>
/// Provider 统一服务：合并配置 CRUD（原 ProviderConfigStore）和 Provider 实例管理（原 ProviderClientFactory）。
/// <para>
/// 重构后不再直接暴露底层 <c>IChatClient</c>/<c>IEmbeddingGenerator</c>——调用方通过
/// <see cref="GetProvider"/> / <see cref="GetDefaultProvider"/> / <see cref="GetDefaultEmbeddingProvider"/>
/// 拿到高层 <see cref="MicroProvider"/>，内部完成消息发送、Agent 循环与 usage 追踪。
/// 实例按 (ProtocolId, ModelType, ConfigHash) 缓存；任一配置字段变化都会触发旧实例 dispose 与新实例重建。
/// </para>
/// <para>
/// 生命周期：<see cref="MicroService"/> 子类，按 <see cref="Order"/>=15 在会话服务之前启动、之后停止。
/// </para>
/// </summary>
public sealed class ProviderService : MicroService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ReaderWriterLockSlim _configLock = new(LockRecursionPolicy.NoRecursion);
    private readonly ConcurrentDictionary<CacheKey, MicroProvider> _providerCache = new();

    private IUsageTracker? _usageTracker;

    /// <summary>通过 DI 注入 <see cref="IServiceProvider"/>，运行时依赖在 <see cref="StartAsync"/> 中惰性解析。</summary>
    public ProviderService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public override int Order => 15;

    /// <inheritdoc />
    protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _usageTracker ??= _serviceProvider.GetRequiredService<IUsageTracker>();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        List<MicroProvider> snapshot = [.. _providerCache.Values];
        _providerCache.Clear();

        List<Exception> errors = [];
        foreach (MicroProvider provider in snapshot)
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        ThrowIfNeeded(errors);
    }

    // ── Provider 实例获取 ───────────────────────────────────────────────

    /// <summary>
    /// 按 <see cref="ProviderConfig.Id"/> 查找启用中的 Chat Provider。
    /// </summary>
    /// <exception cref="InvalidOperationException">Id 未命中或对应 Provider 已禁用。</exception>
    /// <exception cref="NotSupportedException">配置的 <see cref="ProviderProtocol"/> / <see cref="ModelType"/> 组合不被支持。</exception>
    public ChatMicroProvider GetProvider(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ProviderConfig? cfg = GetById(id);
        if (cfg is null)
            throw new InvalidOperationException($"Provider '{id}' not found.");
        if (!cfg.IsEnabled)
            throw new InvalidOperationException($"Provider '{id}' is disabled.");
        if (cfg.ModelType != ModelType.Chat)
            throw new InvalidOperationException(
                $"Provider '{id}' is of ModelType '{cfg.ModelType}', not Chat. Use GetEmbeddingProvider instead.");
        return (ChatMicroProvider)GetOrCreate(cfg);
    }

    /// <summary>尝试按 <paramref name="id"/> 获取 Chat Provider；未命中或已禁用时返回 <c>null</c>。</summary>
    public ChatMicroProvider? TryGetProvider(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        ProviderConfig? cfg = GetById(id);
        if (cfg is null || !cfg.IsEnabled || cfg.ModelType != ModelType.Chat) return null;
        return (ChatMicroProvider)GetOrCreate(cfg);
    }

    /// <summary>获取默认（或首个启用的）Chat Provider；无可用 Provider 时返回 <c>null</c>。</summary>
    public ChatMicroProvider? GetDefaultProvider()
    {
        ProviderConfig? cfg = GetDefault();
        return cfg is null ? null : (ChatMicroProvider)GetOrCreate(cfg);
    }

    /// <summary>获取默认（或首个启用的）Embedding Provider；无可用 Provider 时返回 <c>null</c>。</summary>
    public EmbeddingMicroProvider? GetDefaultEmbeddingProvider()
    {
        ProviderConfig? cfg = GetEmbeddingProviders()
            .OrderByDescending(p => p.IsDefault)
            .FirstOrDefault();
        return cfg is null ? null : (EmbeddingMicroProvider)GetOrCreate(cfg);
    }

    /// <summary>按 id 获取 Embedding Provider。</summary>
    public EmbeddingMicroProvider? TryGetEmbeddingProvider(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        ProviderConfig? cfg = GetById(id);
        if (cfg is null || !cfg.IsEnabled || cfg.ModelType != ModelType.Embedding) return null;
        return (EmbeddingMicroProvider)GetOrCreate(cfg);
    }

    // ── 缓存（按 config 哈希）──────────────────────────────────────────
    private MicroProvider GetOrCreate(ProviderConfig config)
    {
        if (_usageTracker is null)
            throw new InvalidOperationException(
                "ProviderService is not started. Ensure it is registered as MicroService and the engine is running.");

        CacheKey key = CacheKey.From(config);
        if (_providerCache.TryGetValue(key, out MicroProvider? cached))
            return cached;

        MicroProvider created = Build(config, _usageTracker);
        MicroProvider actual = _providerCache.GetOrAdd(key, created);
        if (!ReferenceEquals(created, actual))
        {
            // 其它线程已插入：释放刚构造的多余实例。
            _ = created.DisposeAsync().AsTask();
        }
        else
        {
            // 新实例上位：把同一 (ProviderId, ModelType) 的旧 hash 清理并释放。
            PurgeStaleEntries(config.Id, config.ModelType, keep: key);
        }

        return actual;
    }

    private static MicroProvider Build(ProviderConfig config, IUsageTracker tracker)
    {
        return (config.Protocol, config.ModelType) switch
        {
            (ProviderProtocol.OpenAI, ModelType.Chat) => new OpenAIChatMicroProvider(config, tracker),
            (ProviderProtocol.Anthropic, ModelType.Chat) => new AnthropicChatMicroProvider(config, tracker),
            (ProviderProtocol.OpenAI, ModelType.Embedding) => new OpenAIEmbeddingMicroProvider(config, tracker),
            _ => throw new NotSupportedException(
                $"No MicroProvider is registered for protocol '{config.Protocol}' and ModelType '{config.ModelType}'."),
        };
    }

    /// <summary>清理同一 (ProviderId, ModelType) 下除 <paramref name="keep"/> 外的旧缓存条目。</summary>
    private void PurgeStaleEntries(string providerId, ModelType modelType, CacheKey keep)
    {
        foreach (KeyValuePair<CacheKey, MicroProvider> kv in _providerCache)
        {
            if (kv.Key.Equals(keep)) continue;
            if (!string.Equals(kv.Key.ProviderId, providerId, StringComparison.Ordinal)) continue;
            if (kv.Key.ModelType != modelType) continue;

            if (_providerCache.TryRemove(kv.Key, out MicroProvider? stale))
                _ = stale.DisposeAsync().AsTask();
        }
    }

    // ── Config CRUD（保持与旧实现一致）──────────────────────────────────

    /// <summary>返回配置快照列表（解析环境变量后）。</summary>
    public IReadOnlyList<ProviderConfig> All
    {
        get
        {
            _configLock.EnterReadLock();
            try { return GetItems().Select(ToConfig).ToList().AsReadOnly(); }
            finally { _configLock.ExitReadLock(); }
        }
    }

    /// <summary>返回第一个启用的非 Embedding Provider（按 IsDefault 降序）。</summary>
    public ProviderConfig? GetDefault()
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && p.ModelType != "embedding")
                .OrderByDescending(p => p.IsDefault)
                .Select(ToConfig)
                .FirstOrDefault();
        }
        finally { _configLock.ExitReadLock(); }
    }

    /// <summary>返回全部启用的 Embedding Provider。</summary>
    public IReadOnlyList<ProviderConfig> GetEmbeddingProviders()
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && p.ModelType == "embedding")
                .Select(ToConfig)
                .ToList()
                .AsReadOnly();
        }
        finally { _configLock.ExitReadLock(); }
    }

    /// <summary>按 id 查找配置（解析环境变量后）。</summary>
    public ProviderConfig? GetById(string id)
    {
        _configLock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToConfig(e) : null; }
        finally { _configLock.ExitReadLock(); }
    }

    /// <summary>新增 Provider 配置，返回带分配 id 的副本。</summary>
    public ProviderConfig Add(ProviderConfig config)
    {
        var entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });

        _configLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any()) entity = entity with { IsDefault = true };
            MicroClawConfig.Save(new ProvidersOptions { Items = [.. opts.Items, entity] });
        }
        finally { _configLock.ExitWriteLock(); }

        return ToConfig(entity);
    }

    /// <summary>更新 Provider 配置（留空的 ApiKey 不覆盖旧值）；未命中返回 null。</summary>
    public ProviderConfig? Update(string id, ProviderConfig incoming)
    {
        _configLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == id);
            if (idx < 0) return null;

            var current = opts.Items[idx];
            var updated = current with
            {
                DisplayName = incoming.DisplayName,
                Protocol = SerializeProtocol(incoming.Protocol),
                ModelType = SerializeModelType(incoming.ModelType),
                BaseUrl = string.IsNullOrWhiteSpace(incoming.BaseUrl) ? null : incoming.BaseUrl,
                ModelName = incoming.ModelName,
                MaxOutputTokens = incoming.MaxOutputTokens,
                IsEnabled = incoming.IsEnabled,
                CapabilitiesJson = JsonSerializer.Serialize(incoming.Capabilities),
                ApiKey = !string.IsNullOrWhiteSpace(incoming.ApiKey) && incoming.ApiKey != "***"
                    ? incoming.ApiKey
                    : current.ApiKey,
            };
            var newItems = new List<ProviderConfigEntity>(opts.Items) { [idx] = updated };
            MicroClawConfig.Save(new ProvidersOptions { Items = newItems });
            return ToConfig(updated);
        }
        finally { _configLock.ExitWriteLock(); }
    }

    /// <summary>删除 Provider 配置；未命中返回 false。</summary>
    public bool Delete(string id)
    {
        _configLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            MicroClawConfig.Save(new ProvidersOptions { Items = opts.Items.Where(e => e.Id != id).ToList() });
            return true;
        }
        finally { _configLock.ExitWriteLock(); }
    }

    /// <summary>将指定 id 置为默认 Provider；未命中返回 false。</summary>
    public bool SetDefault(string id)
    {
        _configLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            var newItems = opts.Items.Select(e => e with { IsDefault = e.Id == id }).ToList();
            MicroClawConfig.Save(new ProvidersOptions { Items = newItems });
            return true;
        }
        finally { _configLock.ExitWriteLock(); }
    }

    // ── Private Helpers ─────────────────────────────────────────────────
    private static List<ProviderConfigEntity> GetItems() => MicroClawConfig.Get<ProvidersOptions>().Items;

    private static ProviderConfig ToConfig(ProviderConfigEntity e) =>
        new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Protocol = ParseProtocol(e.Protocol),
            ModelType = ParseModelType(e.ModelType),
            BaseUrl = string.IsNullOrWhiteSpace(e.BaseUrl) ? null : e.BaseUrl,
            ApiKey = ResolveEnvVars(e.ApiKey) ?? string.Empty,
            ModelName = ResolveEnvVars(e.ModelName) ?? string.Empty,
            MaxOutputTokens = e.MaxOutputTokens,
            IsEnabled = e.IsEnabled,
            IsDefault = e.IsDefault,
            Capabilities = DeserializeCapabilities(e.CapabilitiesJson)
        };

    private static ProviderConfigEntity ToEntity(ProviderConfig c) =>
        new()
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            Protocol = SerializeProtocol(c.Protocol),
            ModelType = SerializeModelType(c.ModelType),
            BaseUrl = string.IsNullOrWhiteSpace(c.BaseUrl) ? null : c.BaseUrl,
            ApiKey = c.ApiKey,
            ModelName = c.ModelName,
            MaxOutputTokens = c.MaxOutputTokens,
            IsEnabled = c.IsEnabled,
            IsDefault = c.IsDefault,
            CapabilitiesJson = JsonSerializer.Serialize(c.Capabilities)
        };

    private static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            "openai-responses" => ProviderProtocol.OpenAI,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };

    private static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };

    private static ModelType ParseModelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "embedding" => ModelType.Embedding,
            _ => ModelType.Chat
        };

    private static string SerializeModelType(ModelType modelType) =>
        modelType switch
        {
            ModelType.Embedding => "embedding",
            _ => "chat"
        };

    private static ProviderCapabilities DeserializeCapabilities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProviderCapabilities();
        try { return JsonSerializer.Deserialize<ProviderCapabilities>(json) ?? new ProviderCapabilities(); }
        catch { return new ProviderCapabilities(); }
    }

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }

    /// <summary>
    /// 缓存键：按 (ProviderId, ModelType, ConfigHash) 唯一识别一份 provider 实例。
    /// 任一字段变化都会产生新 key，从而触发旧实例 dispose 与新实例创建。
    /// </summary>
    private readonly record struct CacheKey(string ProviderId, ModelType ModelType, int ConfigHash)
    {
        public static CacheKey From(ProviderConfig config) =>
            new(config.Id, config.ModelType, ComputeHash(config));

        private static int ComputeHash(ProviderConfig c)
        {
            // 在 UpdateProvider 场景下，API Key、BaseUrl、ModelName 等任一变化都要求重建实例，
            // 因此把所有会影响底层 SDK 构造与路由的字段纳入哈希。
            var hash = new HashCode();
            hash.Add(c.Protocol);
            hash.Add(c.ModelType);
            hash.Add(c.BaseUrl, StringComparer.Ordinal);
            hash.Add(c.ApiKey, StringComparer.Ordinal);
            hash.Add(c.ModelName, StringComparer.Ordinal);
            hash.Add(c.MaxOutputTokens);
            hash.Add(JsonSerializer.Serialize(c.Capabilities), StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
