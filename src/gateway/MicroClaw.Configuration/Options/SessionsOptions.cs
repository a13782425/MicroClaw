using MicroClaw.Configuration;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 会话元数据列表配置。
/// 通过 <c>sessions.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
[MicroClawYamlConfig("sessions", FileName = "sessions.yaml", IsWritable = true)]
public sealed class SessionsOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 当前系统中保存的会话元数据列表。
    /// </summary>
    [YamlMember(Alias = "items", Description = "当前系统中保存的会话元数据列表。")]
    public List<SessionEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new SessionsOptions();
}
