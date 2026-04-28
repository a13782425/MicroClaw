using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// 统一构造会话执行所需的 <see cref="ChatOptions"/>，供 Pet 预装配与 Agent 兜底路径共用。
/// </summary>
public static class ChatExecutionOptionsFactory
{
    public static ChatOptions Build(
        IReadOnlyList<AITool> tools,
        ProviderConfig provider,
        string? modelOverride = null,
        string? effortOverride = null,
        float? temperatureOverride = null,
        float? topPOverride = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(provider);

        var options = new ChatOptions
        {
            ModelId = modelOverride ?? provider.ModelName,
            MaxOutputTokens = provider.MaxOutputTokens,
            Temperature = temperatureOverride,
            TopP = topPOverride,
        };

        if (!string.IsNullOrWhiteSpace(effortOverride))
            options.AdditionalProperties ??= new() { ["thinking_effort"] = effortOverride };

        if (tools.Count > 0 && provider.Capabilities.Features.HasFlag(ProviderFeature.FunctionCalling))
            options.Tools = [.. tools];

        options.ToolMode = ChatToolMode.Auto;
        options.AllowMultipleToolCalls = true;
        return options;
    }
}
