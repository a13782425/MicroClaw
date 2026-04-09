using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Sessions;

/// <summary>
/// Global Web channel provider backed by SignalR.
/// </summary>
public sealed class WebChannelProvider(IHubContext<GatewayHub> hubContext) : IChannelProvider
{
    public string Name => "Web";

    public ChannelType Type => ChannelType.Web;

    public string DisplayName => "Web";

    public bool CanCreate => false;

    public IChannel Create(ChannelEntity config) => new WebChannel(config, hubContext);

    public async Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients.All.SendAsync("channelMessage", new
        {
            sessionId = message.UserId,
            content = message.Content
        }, cancellationToken);
    }

    public Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(true, "Web channel is in-process", 0));
}

public sealed class WebChannel(ChannelEntity config, IHubContext<GatewayHub> hubContext) : IChannel
{
    public string Id => Config.Id;

    public string Name => "Web";

    public ChannelType Type => ChannelType.Web;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "Web" : Config.DisplayName;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
        => hubContext.Clients.All.SendAsync("channelMessage", new
        {
            sessionId = message.UserId,
            content = message.Content
        }, cancellationToken);

    public Task<string?> HandleWebhookAsync(string body, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(true, "Web channel is in-process", 0));
}
