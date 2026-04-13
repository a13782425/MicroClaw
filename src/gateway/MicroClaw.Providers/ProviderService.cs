using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers.Claude;
using MicroClaw.Providers.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Providers;

/// <summary>
/// Provider 统一服务：合并配置 CRUD（原 ProviderConfigStore）和客户端工厂（原 ProviderClientFactory）。
/// Provider 实现（OpenAI / Anthropic）在构造函数中内部创建，不再逐个注册到 DI。
/// 对标 ChannelService 的模式——单一服务持有所有 Provider 实例。
/// </summary>
public sealed class ProviderService
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly IReadOnlyDictionary<ProviderProtocol, IModelProvider> _providers;

    public ProviderService(ILoggerFactory loggerFactory)
    {
        // Build providers internally — no longer injected from DI
        _providers = new Dictionary<ProviderProtocol, IModelProvider>
        {
            [ProviderProtocol.OpenAI] = new OpenAIModelProvider(loggerFactory),
            [ProviderProtocol.Anthropic] = new AnthropicModelProvider(loggerFactory),
        };
    }

    // ── Client Factory ──────────────────────────────────────────────────

    /// <summary>
    /// Dispatches IChatClient creation to the registered <see cref="IModelProvider"/> that
    /// supports the requested <see cref="ProviderProtocol"/>.
    /// </summary>
    public IChatClient CreateClient(ProviderConfig config)
    {
        if (!_providers.TryGetValue(config.Protocol, out IModelProvider? provider))
            throw new NotSupportedException(
                $"Protocol '{config.Protocol}' is not supported. " +
                "Ensure the corresponding IModelProvider is registered in ProviderService.");

        return provider.Create(config);
    }

    // ── Config CRUD ─────────────────────────────────────────────────────

    public IReadOnlyList<ProviderConfig> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return GetItems().Select(ToConfig).ToList().AsReadOnly(); }
            finally { _lock.ExitReadLock(); }
        }
    }

    public ProviderConfig? GetDefault()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && p.ModelType != "embedding")
                .OrderByDescending(p => p.IsDefault)
                .Select(ToConfig)
                .FirstOrDefault();
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<ProviderConfig> GetEmbeddingProviders()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(p => p.IsEnabled && p.ModelType == "embedding")
                .Select(ToConfig)
                .ToList()
                .AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    public ProviderConfig? GetById(string id)
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToConfig(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    public ProviderConfig Add(ProviderConfig config)
    {
        var entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });

        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            // First added provider auto-set as default
            if (!opts.Items.Any()) entity = entity with { IsDefault = true };
            MicroClawConfig.Save(new ProvidersOptions { Items = [.. opts.Items, entity] });
        }
        finally { _lock.ExitWriteLock(); }

        return ToConfig(entity);
    }

    public ProviderConfig? Update(string id, ProviderConfig incoming)
    {
        _lock.EnterWriteLock();
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
        finally { _lock.ExitWriteLock(); }
    }

    public bool Delete(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            MicroClawConfig.Save(new ProvidersOptions { Items = opts.Items.Where(e => e.Id != id).ToList() });
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool SetDefault(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ProvidersOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            var newItems = opts.Items.Select(e => e with { IsDefault = e.Id == id }).ToList();
            MicroClawConfig.Save(new ProvidersOptions { Items = newItems });
            return true;
        }
        finally { _lock.ExitWriteLock(); }
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
}
