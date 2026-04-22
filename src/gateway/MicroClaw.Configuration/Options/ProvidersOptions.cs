using MicroClaw.Configuration;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// Model Provider 配置列表。
/// 通过 <c>providers.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
[MicroClawYamlConfig("providers", FileName = "providers.yaml", IsWritable = true)]
public sealed class ProvidersOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 当前系统中可用的 Provider 配置列表。
    /// </summary>
    [YamlMember(Alias = "items", Description = "当前系统中可用的 Provider 配置列表。")]
    public List<ProviderConfigEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new ProvidersOptions();
}
