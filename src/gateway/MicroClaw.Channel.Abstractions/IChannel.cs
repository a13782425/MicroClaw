using MicroClaw.Channel.Abstractions.Models;

namespace MicroClaw.Channel.Abstractions;

public interface IChannel
{
    string Name { get; }

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);
}