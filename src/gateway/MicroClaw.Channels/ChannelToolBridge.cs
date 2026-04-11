using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Options;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Channels;

/// <summary>
/// Bridges <see cref="IChannelProvider.GetToolDescriptions"/> and
/// <see cref="IChannelProvider.CreateToolsAsync(string, CancellationToken)"/>
/// to the <see cref="IToolProvider"/> interface consumed by <c>ToolCollector</c>.
/// Delegates to the matching provider based on <see cref="ToolCreationContext.ChannelType"/>.
/// </summary>
public sealed class ChannelToolBridge(ChannelService channelService) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Channel;
    public string GroupId => "channel";
    public string DisplayName => "渠道工具";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions()
    {
        List<(string Name, string Description)> all = [];
        foreach (IChannelProvider provider in channelService.GetProviders())
        {
            all.AddRange(provider.GetToolDescriptions());
        }
        return all;
    }

    public async Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (context.ChannelType is null || string.IsNullOrWhiteSpace(context.ChannelId))
            return ToolProviderResult.Empty;

        IChannelProvider provider;
        try
        {
            provider = channelService.GetRequiredProvider(context.ChannelType.Value);
        }
        catch
        {
            return ToolProviderResult.Empty;
        }

        IReadOnlyList<AIFunction> tools = await provider.CreateToolsAsync(context.ChannelId, ct);
        if (tools.Count == 0)
            return ToolProviderResult.Empty;

        return new ToolProviderResult(tools.Cast<AITool>().ToList());
    }
}
