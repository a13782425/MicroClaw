using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 会话元数据列表配置。
/// 通过 <c>sessions.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
[MicroClawYamlConfig("sessions", FileName = "sessions.yaml", IsWritable = true)]
public sealed class SessionsOptions : IMicroClawConfigOptions
{
    [ConfigurationKeyName("items")]
    public List<SessionEntity> Items { get; set; } = [];
}
