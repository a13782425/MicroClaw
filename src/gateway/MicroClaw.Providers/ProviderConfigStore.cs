using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Providers;

public sealed class ProviderConfigStore(string configDir)
    : YamlFileStore<ProviderConfigEntity>(Path.Combine(configDir, "providers.yaml"), e => e.Id)
{
    public IReadOnlyList<ProviderConfig> All
        => GetAll().Select(ToConfig).ToList().AsReadOnly();

    /// <summary>
    /// 杩斿洖褰撳墠璁剧疆涓洪粯璁や笖宸插惎鐢ㄧ殑 Chat 绫诲瀷 Provider锛涜嫢鏃犻粯璁ゅ垯杩斿洖绗竴涓凡鍚敤鐨?Chat Provider銆?
    /// </summary>
    public ProviderConfig? GetDefault()
        => GetAll()
            .Where(p => p.IsEnabled && p.ModelType != "embedding")
            .OrderByDescending(p => p.IsDefault)
            .Select(ToConfig)
            .FirstOrDefault();

    /// <summary>杩斿洖鎵€鏈夊凡鍚敤鐨?Embedding 绫诲瀷 Provider锛屼緵 RAG 绛夋湇鍔¤皟鐢ㄣ€?/summary>
    public IReadOnlyList<ProviderConfig> GetEmbeddingProviders()
        => GetAll()
            .Where(p => p.IsEnabled && p.ModelType == "embedding")
            .Select(ToConfig)
            .ToList()
            .AsReadOnly();

    public ProviderConfig? GetById(string id)
        => GetYamlById(id) is { } e ? ToConfig(e) : null;

    public ProviderConfig Add(ProviderConfig config)
    {
        ProviderConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        ExecuteWrite(items =>
        {
            // First added provider auto-set as default
            if (!items.Any()) entity.IsDefault = true;
            items[entity.Id] = entity;
            return true;
        });
        return ToConfig(entity);
    }

    public ProviderConfig? Update(string id, ProviderConfig incoming)
    {
        var updated = MutateYaml(id, e =>
        {
            e.DisplayName = incoming.DisplayName;
            e.Protocol = SerializeProtocol(incoming.Protocol);
            e.ModelType = SerializeModelType(incoming.ModelType);
            e.BaseUrl = string.IsNullOrWhiteSpace(incoming.BaseUrl) ? null : incoming.BaseUrl;
            e.ModelName = incoming.ModelName;
            e.MaxOutputTokens = incoming.MaxOutputTokens;
            e.IsEnabled = incoming.IsEnabled;
            e.CapabilitiesJson = JsonSerializer.Serialize(incoming.Capabilities);

            if (!string.IsNullOrWhiteSpace(incoming.ApiKey) && incoming.ApiKey != "***")
                e.ApiKey = incoming.ApiKey;
        });
        return updated is null ? null : ToConfig(updated);
    }

    public bool Delete(string id) => RemoveYaml(id);

    public bool SetDefault(string id)
    {
        if (!ContainsYaml(id)) return false;
        ExecuteWrite(items =>
        {
            foreach (var e in items.Values)
                e.IsDefault = e.Id == id;
            return true;
        });
        return true;
    }

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
