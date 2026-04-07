using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Sessions;

/// <summary>
/// Web 渠道的 <see cref="ISessionChannel"/> 实现。
/// <para>
/// Web 渠道没有对应的外部 SDK，消息通过 SignalR 推送到前端。
/// 通过 <c>IHubContext&lt;GatewayHub&gt;</c> 向所有前端客户端广播消息。
/// </para>
/// </summary>
public sealed class WebSessionChannel(IHubContext<GatewayHub> hubContext) : ISessionChannel
{
    public string ChannelId => "web";
    public ChannelType Type => ChannelType.Web;
    public string DisplayName => "Web";

    public async Task PublishTextAsync(string recipientId, string content, CancellationToken ct = default)
    {
        await hubContext.Clients.All.SendAsync("channelMessage", new
        {
            sessionId = recipientId,
            content
        }, ct);
    }
}
