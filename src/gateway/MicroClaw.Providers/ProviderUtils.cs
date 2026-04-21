using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Configuration.Options;
namespace MicroClaw.Providers;
public static class ProviderUtils
{
    private static Dictionary<string, ProviderConfig> _cache = new();
    private static Dictionary<string, ProviderConfigEntity> _entityCache = new();
    
    public static ProviderConfig ToConfig(this ProviderConfigEntity entity)
    {
        if (_cache.TryGetValue(entity.Id, out var config))
            return config;
        
        config = new ProviderConfig
        {
            Id = entity.Id,
            DisplayName = entity.DisplayName,
            Protocol = ParseProtocol(entity.Protocol),
            ModelType = ParseModelType(entity.ModelType),
            BaseUrl = string.IsNullOrWhiteSpace(entity.BaseUrl) ? null : entity.BaseUrl,
            ApiKey =  ResolveEnvVars(entity.ApiKey) ?? string.Empty,
            ModelName =  ResolveEnvVars(entity.ModelName) ?? string.Empty,
            MaxOutputTokens = entity.MaxOutputTokens,
            IsEnabled = entity.IsEnabled,
            IsDefault = entity.IsDefault,
            Capabilities = DeserializeCapabilities(entity.CapabilitiesJson)
        };
        
        _cache[entity.Id] = config;
        _entityCache[entity.Id] = entity;
        return config;
    }
    
    public static ProviderConfigEntity ToEntity(this ProviderConfig config)
    {
        if (_entityCache.TryGetValue(config.Id, out var entity))
            return entity;
        
        entity = new ProviderConfigEntity
        {
            Id = config.Id,
            DisplayName = config.DisplayName,
            Protocol = SerializeProtocol(config.Protocol),
            ModelType = SerializeModelType(config.ModelType),
            BaseUrl = config.BaseUrl,
            ApiKey = config.ApiKey,
            ModelName = config.ModelName,
            MaxOutputTokens = config.MaxOutputTokens,
            IsEnabled = config.IsEnabled,
            IsDefault = config.IsDefault,
            CapabilitiesJson = JsonSerializer.Serialize(config.Capabilities)
        };
        
        _entityCache[config.Id] = entity;
        _cache[config.Id] = config;
        return entity;
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