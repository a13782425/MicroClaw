using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Configuration.Options;
namespace MicroClaw.Providers;
public static class ProviderUtils
{
    private static Dictionary<string, ProviderEntity> _cache = new();
    private static Dictionary<string, ProviderEntityConfig> _entityCache = new();
    
    public static ProviderEntity ToEntity(this ProviderEntityConfig entityConfig)
    {
        if (_cache.TryGetValue(entityConfig.Id, out var config))
            return config;
        
        config = new ProviderEntity
        {
            Id = entityConfig.Id,
            DisplayName = entityConfig.DisplayName,
            Protocol = ParseProtocol(entityConfig.Protocol),
            ModelType = ParseModelType(entityConfig.ModelType),
            BaseUrl = string.IsNullOrWhiteSpace(entityConfig.BaseUrl) ? null : entityConfig.BaseUrl,
            ApiKey =  ResolveEnvVars(entityConfig.ApiKey) ?? string.Empty,
            ModelName =  ResolveEnvVars(entityConfig.ModelName) ?? string.Empty,
            MaxOutputTokens = entityConfig.MaxOutputTokens,
            IsEnabled = entityConfig.IsEnabled,
            IsDefault = entityConfig.IsDefault,
            Capabilities = DeserializeCapabilities(entityConfig.CapabilitiesJson)
        };
        
        _cache[entityConfig.Id] = config;
        _entityCache[entityConfig.Id] = entityConfig;
        return config;
    }
    
    public static ProviderEntityConfig ToConfig(this ProviderEntity entity)
    {
        if (_entityCache.TryGetValue(entity.Id, out var entityConfig))
            return entityConfig;
        
        entityConfig = new ProviderEntityConfig
        {
            Id = entity.Id,
            DisplayName = entity.DisplayName,
            Protocol = SerializeProtocol(entity.Protocol),
            ModelType = SerializeModelType(entity.ModelType),
            BaseUrl = entity.BaseUrl,
            ApiKey = entity.ApiKey,
            ModelName = entity.ModelName,
            MaxOutputTokens = entity.MaxOutputTokens,
            IsEnabled = entity.IsEnabled,
            IsDefault = entity.IsDefault,
            CapabilitiesJson = JsonSerializer.Serialize(entity.Capabilities)
        };
        
        _entityCache[entity.Id] = entityConfig;
        _cache[entity.Id] = entity;
        return entityConfig;
    }
    
    public static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            "openai-responses" => ProviderProtocol.OpenAI,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };
    
    public static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };
    
    public static ModelType ParseModelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "embedding" => ModelType.Embedding,
            _ => ModelType.Chat
        };
    
    public static string SerializeModelType(ModelType modelType) =>
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