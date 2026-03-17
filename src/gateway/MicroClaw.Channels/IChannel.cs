using MicroClaw.Channels.Models;

namespace MicroClaw.Channels;

public interface IChannel
{
    string Name { get; }

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);
}
