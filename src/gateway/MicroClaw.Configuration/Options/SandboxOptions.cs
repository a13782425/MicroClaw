
namespace MicroClaw.Configuration;

/// <summary>
/// 沙盒文件下载 Token 的配置。通过配置节 "sandbox" 绑定。
/// </summary>
[MicroClawYamlConfig("sandbox", FileName = "sandbox.yaml")]
public sealed class SandboxOptions : IMicroClawConfigTemplate
{
    /// <summary>下载 Token 的有效期（分钟），默认 60 分钟。</summary>
    [YamlMember(Alias = "token_expiry_minutes", Description = "下载 Token 的有效期，单位为分钟。")]
    public int TokenExpiryMinutes { get; set; } = 60;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new SandboxOptions();
}
