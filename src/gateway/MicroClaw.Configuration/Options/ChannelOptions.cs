using MicroClaw.Configuration;
namespace MicroClaw.Configuration.Options;
/// <summary>
/// 渠道配置选项。每个渠道实例对应一套配置，包含在 <see cref="SessionsOptions"/> 中。
/// </summary>
[MicroClawYamlConfig("channel", FileName = "channels.yaml", IsWritable = true)]
public sealed class ChannelOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 当前启用或已注册的渠道配置列表。
    /// </summary>
    [YamlMember(Alias = "channels", Description = "当前启用或已注册的渠道配置列表。")]
    public List<ChannelEntity> Channels { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new ChannelOptions();
}