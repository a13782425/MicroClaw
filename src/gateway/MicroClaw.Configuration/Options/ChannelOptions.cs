using Microsoft.Extensions.Configuration;
namespace MicroClaw.Configuration.Options;
/// <summary>
/// 渠道配置选项。每个渠道实例对应一套配置，包含在 <see cref="SessionsOptions"/> 中。
/// </summary>
public sealed class ChannelOptions
{
    [ConfigurationKeyName("channels")]
    public List<ChannelEntity> Channels { get; set; } = [];
}