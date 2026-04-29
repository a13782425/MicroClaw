using System.Text.Json;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;
namespace MicroClaw.Agent;
public static class AgentUtils
{
    
    private static Dictionary<string, AgentEntity> _entityCache = new();
    private static Dictionary<string, AgentEntityConfig> _configCache = new();
    
    public static AgentEntity ToEntity(this AgentEntityConfig entityConfig)
    {
        if (_entityCache.TryGetValue(entityConfig.Id, out var config))
            return config;
        
        config = new AgentEntity(entityConfig);
        _entityCache[entityConfig.Id] = config;
        _configCache[entityConfig.Id] = entityConfig;
        return config;
    }
    
    public static AgentEntityConfig ToConfig(this AgentEntity entity)
    {
        var config =  entity.Config;
        _entityCache[entity.Id] = entity;
        _configCache[entity.Id] = config;
        return config;
    }
    public static ProviderRoutingStrategy ParseRoutingStrategy(string? value) => Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out var result) ? result : ProviderRoutingStrategy.Default;
}