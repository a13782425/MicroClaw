using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Sessions;

/// <summary>
/// Shared Web channel implementation backed by SignalR.
/// </summary>
public sealed class WebChannel(IHubContext<GatewayHub> hubContext) : IChannel
{
    public string Name => "Web";

    public ChannelType Type => ChannelType.Web;

    public string DisplayName => "Web";

    public bool CanCreate => false;

    public async Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients.All.SendAsync("channelMessage", new
        {
            sessionId = message.UserId,
            content = message.Content
        }, cancellationToken);
    }

    public Task<string?> HandleWebhookAsync(string body, ChannelEntity channelEntity, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity channelEntity, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(true, "Web channel is in-process", 0));
}
