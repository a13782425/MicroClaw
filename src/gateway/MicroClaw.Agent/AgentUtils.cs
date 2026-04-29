using System.Text.Json;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;
namespace MicroClaw.Agent;
public static class AgentUtils
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static Dictionary<string, AgentEntity> _cache = new();
    private static Dictionary<string, AgentEntityConfig> _entityCache = new();
    
    public static AgentEntity ToEntity(this AgentEntityConfig entityConfig)
    {
        if (_cache.TryGetValue(entityConfig.Id, out var config))
            return config;
        
        config = AgentEntity.Reconstitute(
            id: entityConfig.Id, 
            name: entityConfig.Name, 
            description: entityConfig.Description, 
            isEnabled: entityConfig.IsEnabled, 
            disabledSkillIds: DeserializeList<string>(entityConfig.DisabledSkillIdsJson), 
            disabledMcpServerIds: DeserializeList<string>(entityConfig.DisabledMcpServerIdsJson), 
            toolGroupConfigs: DeserializeList<ToolGroupConfig>(entityConfig.ToolGroupConfigsJson), 
            createdAtUtc: TimeUtils.FromMs(entityConfig.CreatedAtMs), 
            isDefault: entityConfig.IsDefault, 
            contextWindowMessages: entityConfig.ContextWindowMessages, 
            exposeAsA2A: entityConfig.ExposeAsA2A, 
            allowedSubAgentIds: DeserializeNullableList<string>(entityConfig.AllowedSubAgentIdsJson),
            routingStrategy: ParseRoutingStrategy(entityConfig.RoutingStrategy), 
            monthlyBudgetUsd: entityConfig.MonthlyBudgetUsd);
        
        _cache[entityConfig.Id] = config;
        _entityCache[entityConfig.Id] = entityConfig;
        return config;
    }
    
    public static AgentEntityConfig ToConfig(this AgentEntity entity)
    {
        if (_entityCache.TryGetValue(entity.Id, out var entityConfig))
            return entityConfig;
        
        entityConfig = new AgentEntityConfig
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            DisabledSkillIdsJson = entity.DisabledSkillIds.Count > 0 ? JsonSerializer.Serialize(entity.DisabledSkillIds, JsonOpts) : null,
            DisabledMcpServerIdsJson = entity.DisabledMcpServerIds.Count > 0 ? JsonSerializer.Serialize(entity.DisabledMcpServerIds, JsonOpts) : null,
            ToolGroupConfigsJson = entity.ToolGroupConfigs.Count > 0 ? JsonSerializer.Serialize(entity.ToolGroupConfigs, JsonOpts) : null,
            CreatedAtMs = TimeUtils.ToMs(entity.CreatedAtUtc),
            IsDefault = entity.IsDefault,
            ContextWindowMessages = entity.ContextWindowMessages,
            ExposeAsA2A = entity.ExposeAsA2A,
            AllowedSubAgentIdsJson = entity.AllowedSubAgentIds is not null ? JsonSerializer.Serialize(entity.AllowedSubAgentIds, JsonOpts) : null,
            RoutingStrategy = entity.RoutingStrategy == ProviderRoutingStrategy.Default ? null : entity.RoutingStrategy.ToString(),
            MonthlyBudgetUsd = entity.MonthlyBudgetUsd,
        };
        
        _entityCache[entity.Id] = entityConfig;
        _cache[entity.Id] = entity;
        return entityConfig;
    }
    
    private static IReadOnlyList<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }
    
    private static IReadOnlyList<T>? DeserializeNullableList<T>(string? json)
    {
        if (json is null) return null;
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }
    
    private static ProviderRoutingStrategy ParseRoutingStrategy(string? value) => Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out var result) ? result : ProviderRoutingStrategy.Default;
}