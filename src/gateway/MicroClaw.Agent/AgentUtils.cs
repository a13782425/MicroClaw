using System.Text.Json;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;
namespace MicroClaw.Agent;
public static class AgentUtils
{
    public static AgentEntity ToEntity(this AgentEntityConfig entityConfig)
        => new(entityConfig with { });
    
    public static AgentEntityConfig ToConfig(this AgentEntity entity)
        => entity.Config with { };

    public static ProviderRoutingStrategy ParseRoutingStrategy(string? value) => Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out var result) ? result : ProviderRoutingStrategy.Default;
}