using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Channel;
public interface IChannelService : IService
{
    IChannel GetRequired(string channelId);
    
    bool TryGet(string channelId, out IChannel? channel);
    
    IReadOnlyList<IChannel> GetByType(ChannelType type);
    
    IReadOnlyList<IChannelProvider> GetProviders();
    
    IChannelProvider GetRequiredProvider(ChannelType type);
}