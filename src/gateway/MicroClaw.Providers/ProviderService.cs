using System.Collections.Concurrent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Providers;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
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
    /// 按 <see cref="ProviderDto.Id"/> 查找启用中的 Chat Provider。
    /// </summary>
    /// <exception cref="InvalidOperationException">Id 未命中或对应 Provider 已禁用。</exception>
    /// <exception cref="NotSupportedException">配置的 <see cref="ProviderProtocol"/> / <see cref="ModelType"/> 组合不被支持。</exception>
    public ChatMicroProvider GetProvider(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ProviderEntityConfig? cfg = GetConfigById(id);
        if (cfg is null)
            throw new InvalidOperationException($"Provider '{id}' not found.");
        if (!cfg.IsEnabled)
            throw new InvalidOperationException($"Provider '{id}' is disabled.");
        if (ProviderUtils.ParseModelType(cfg.ModelType) != ModelType.Chat)
            throw new InvalidOperationException($"Provider '{id}' is of ModelType '{cfg.ModelType}', not Chat. Use GetEmbeddingProvider instead.");
        return (ChatMicroProvider)GetOrCreate(cfg);
    }
    
    /// <summary>尝试按 <paramref name="id"/> 获取 Chat Provider；未命中或已禁用时返回 <c>null</c>。</summary>
    public ChatMicroProvider? TryGetProvider(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        ProviderEntityConfig? cfg = GetConfigById(id);
        if (cfg is null || !cfg.IsEnabled || ProviderUtils.ParseModelType(cfg.ModelType) != ModelType.Chat) return null;
        return (ChatMicroProvider)GetOrCreate(cfg);
    }
    
    /// <summary>获取默认（或首个启用的）Chat Provider；无可用 Provider 时返回 <c>null</c>。</summary>
    public ChatMicroProvider? GetDefaultProvider()
    {
        ProviderEntityConfig? cfg = GetDefaultConfig();
        return cfg is null ? null : (ChatMicroProvider)GetOrCreate(cfg);
    }
    
    /// <summary>获取默认（或首个启用的）Embedding Provider；无可用 Provider 时返回 <c>null</c>。</summary>
    public EmbeddingMicroProvider? GetDefaultEmbeddingProvider()
    {
        ProviderEntityConfig? cfg = GetEmbeddingConfigs().OrderByDescending(p => p.IsDefault).FirstOrDefault();
        return cfg is null ? null : (EmbeddingMicroProvider)GetOrCreate(cfg);
    }
    
    /// <summary>按 id 获取 Embedding Provider。</summary>
    public EmbeddingMicroProvider? TryGetEmbeddingProvider(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        ProviderEntityConfig? cfg = GetConfigById(id);
        if (cfg is null || !cfg.IsEnabled || ProviderUtils.ParseModelType(cfg.ModelType) != ModelType.Embedding) return null;
        return (EmbeddingMicroProvider)GetOrCreate(cfg);
    }
    
    // ── 缓存（按 config 哈希）──────────────────────────────────────────
    private MicroProvider GetOrCreate(ProviderEntityConfig cfg)
    {
        if (_usageTracker is null)
            throw new InvalidOperationException("ProviderService is not started. Ensure it is registered as MicroService and the engine is running.");
        
        CacheKey key = CacheKey.From(cfg);
        if (_providerCache.TryGetValue(key, out MicroProvider? cached))
            return cached;
        
        MicroProvider created = Build(cfg, _usageTracker);
        MicroProvider actual = _providerCache.GetOrAdd(key, created);
        if (!ReferenceEquals(created, actual))
        {
            // 其它线程已插入：释放刚构造的多余实例。
            _ = created.DisposeAsync().AsTask();
        }
        else
        {
            // 新实例上位：把同一 (ProviderId, ModelType) 的旧 hash 清理并释放。
            PurgeStaleEntries(cfg.Id, ProviderUtils.ParseModelType(cfg.ModelType), keep: key);
        }
        
        return actual;
    }
    
    private static MicroProvider Build(ProviderEntityConfig cfg, IUsageTracker tracker)
    {
        return (ProviderUtils.ParseProtocol(cfg.Protocol), ProviderUtils.ParseModelType(cfg.ModelType)) switch
        {
            (ProviderProtocol.OpenAI, ModelType.Chat) => new OpenAIChatMicroProvider(cfg, tracker),
            (ProviderProtocol.Anthropic, ModelType.Chat) => new AnthropicChatMicroProvider(cfg, tracker),
            (ProviderProtocol.OpenAI, ModelType.Embedding) => new OpenAIEmbeddingMicroProvider(cfg, tracker),
            _ => throw new NotSupportedException($"No MicroProvider is registered for protocol '{cfg.Protocol}' and ModelType '{cfg.ModelType}'."),
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
    
    // ── Config CRUD ──────────────────────────────────────────────────────
    
    /// <summary>返回配置快照列表（原始 <see cref="ProviderEntityConfig"/>，endpoint 层负责映射为 DTO）。</summary>
    public IReadOnlyList<ProviderEntityConfig> All
    {
        get
        {
            _configLock.EnterReadLock();
            try
            {
                return GetItems().ToList().AsReadOnly();
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }
    }
    
    /// <summary>返回第一个启用的非 Embedding Provider 的原始配置（按 IsDefault 降序）。</summary>
    public ProviderEntityConfig? GetDefault() => GetDefaultConfig();
    
    /// <summary>返回全部启用的 Embedding Provider 的原始配置列表。</summary>
    public IReadOnlyList<ProviderEntityConfig> GetEmbeddingProviders() => GetEmbeddingConfigs();
    
    /// <summary>按 id 查找原始配置。</summary>
    public ProviderEntityConfig? GetById(string id) => GetConfigById(id);
    
    /// <summary>新增 Provider 配置，自动分配 Id，返回带 Id 的副本。</summary>
    public ProviderEntityConfig Add(ProviderEntityConfig incoming)
    {
        var entityConfig = incoming with { Id = Guid.NewGuid().ToString("N") };
        
        _configLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any())
                entityConfig = entityConfig with { IsDefault = true };
            MicroClawConfig.Save(new ProvidersOptions { Items = [.. opts.Items, entityConfig] });
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
        
        return entityConfig;
    }
    
    /// <summary>更新 Provider 配置（ApiKey 为空或 "***" 时保留旧值）；未命中返回 null。</summary>
    public ProviderEntityConfig? Update(string id, ProviderEntityConfig incoming)
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
                Protocol = incoming.Protocol,
                ModelType = incoming.ModelType,
                BaseUrl = incoming.BaseUrl,
                ModelName = incoming.ModelName,
                MaxOutputTokens = incoming.MaxOutputTokens,
                IsEnabled = incoming.IsEnabled,
                CapabilitiesJson = incoming.CapabilitiesJson,
                ApiKey = !string.IsNullOrWhiteSpace(incoming.ApiKey) && incoming.ApiKey != "***" ? incoming.ApiKey : current.ApiKey,
            };
            var newItems = new List<ProviderEntityConfig>(opts.Items) { [idx] = updated };
            MicroClawConfig.Save(new ProvidersOptions { Items = newItems });
            return updated;
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    /// <summary>返回全部启用的 Chat Provider 实例列表，供 Agent 路由器和业务层直接使用。</summary>
    public IReadOnlyList<ChatMicroProvider> GetEnabledChatProviders()
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && !string.Equals(p.ModelType, "embedding", StringComparison.OrdinalIgnoreCase))
                .Select(cfg => (ChatMicroProvider)GetOrCreate(cfg))
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _configLock.ExitReadLock();
        }
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
        finally
        {
            _configLock.ExitWriteLock();
        }
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
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    // ── Private Helpers ─────────────────────────────────────────────────
    private static List<ProviderEntityConfig> GetItems() => MicroClawConfig.Get<ProvidersOptions>().Items;
    
    private ProviderEntityConfig? GetConfigById(string id)
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Id == id);
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }
    
    private ProviderEntityConfig? GetDefaultConfig()
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && !string.Equals(p.ModelType, "embedding", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.IsDefault)
                .FirstOrDefault();
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }
    
    private IReadOnlyList<ProviderEntityConfig> GetEmbeddingConfigs()
    {
        _configLock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && string.Equals(p.ModelType, "embedding", StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }
    
    /// <summary>
    /// 缓存键：按 (ProviderId, ModelType, ConfigHash) 唯一识别一份 provider 实例。
    /// 任一字段变化都会产生新 key，从而触发旧实例 dispose 与新实例创建。
    /// </summary>
    private readonly record struct CacheKey(string ProviderId, ModelType ModelType, int ConfigHash)
    {
        public static CacheKey From(ProviderEntityConfig cfg) => new(cfg.Id, ProviderUtils.ParseModelType(cfg.ModelType), ComputeHash(cfg));
        
        private static int ComputeHash(ProviderEntityConfig cfg)
        {
            var hash = new HashCode();
            hash.Add(cfg.Protocol, StringComparer.Ordinal);
            hash.Add(cfg.ModelType, StringComparer.Ordinal);
            hash.Add(cfg.BaseUrl, StringComparer.Ordinal);
            hash.Add(cfg.ApiKey, StringComparer.Ordinal);
            hash.Add(cfg.ModelName, StringComparer.Ordinal);
            hash.Add(cfg.MaxOutputTokens);
            hash.Add(cfg.CapabilitiesJson, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}