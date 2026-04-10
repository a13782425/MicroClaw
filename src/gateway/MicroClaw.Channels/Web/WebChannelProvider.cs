using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels;

/// <summary>
/// Global Web channel provider backed by SignalR via <see cref="IMicroHubService"/>.
/// </summary>
public sealed class WebChannelProvider(IMicroHubService hub) : IChannelProvider
{
    public string Name => "Web";

    public ChannelType Type => ChannelType.Web;

    public string DisplayName => "Web";

    public bool CanCreate => false;

    public IChannel Create(ChannelEntity config) => new WebChannel(config, hub);

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => hub.SendAsync("channelMessage", new
        {
            sessionId = message.UserId,
            content   = message.Content
        }, cancellationToken);

    public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
        => Task.FromResult(WebhookResult.Empty);

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(true, "Web channel is in-process", 0));
}

public sealed class WebChannel(ChannelEntity config, IMicroHubService hub) : IChannel
{
    public string Id => Config.Id;

    public string Name => "Web";

    public ChannelType Type => ChannelType.Web;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "Web" : Config.DisplayName;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
        => hub.SendAsync("channelMessage", new
        {
            sessionId = message.UserId,
            content   = message.Content
        }, cancellationToken);

    public Task<WebhookResult> HandleWebhookAsync(string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
        => Task.FromResult(WebhookResult.Empty);

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelDiagnostics.Ok(Config.Id, "web"));

    public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(true, "Web channel is in-process", 0));
}
