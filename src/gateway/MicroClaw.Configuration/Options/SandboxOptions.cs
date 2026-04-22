
namespace MicroClaw.Configuration;

/// <summary>
/// 沙盒文件下载 Token 的配置。通过配置节 "sandbox" 绑定。
/// </summary>
[MicroClawYamlConfig("sandbox")]
public sealed class SandboxOptions : IMicroClawConfigOptions
{
    /// <summary>下载 Token 的有效期（分钟），默认 60 分钟。</summary>
    [YamlMember(Alias = "token_expiry_minutes")]
    public int TokenExpiryMinutes { get; set; } = 60;
}
