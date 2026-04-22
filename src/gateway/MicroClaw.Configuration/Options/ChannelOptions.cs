using MicroClaw.Configuration;
namespace MicroClaw.Configuration.Options;
/// <summary>
/// 渠道配置选项。每个渠道实例对应一套配置，包含在 <see cref="SessionsOptions"/> 中。
/// </summary>
[MicroClawYamlConfig("channel", FileName = "channels.yaml", IsWritable = true)]
public sealed class ChannelOptions : IMicroClawConfigTemplate
{
    [YamlMember(Alias = "channels")]
    public List<ChannelEntity> Channels { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new ChannelOptions();
}