using MicroClaw.Providers;

namespace MicroClaw.Agent;

public static class AgentUtils
{
    public static ProviderRoutingStrategy ParseRoutingStrategy(string? value) =>
        Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out var result) ? result : ProviderRoutingStrategy.Default;
}