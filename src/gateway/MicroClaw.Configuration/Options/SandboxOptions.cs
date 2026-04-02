using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

/// <summary>
/// 沙盒文件下载 Token 的配置。通过配置节 "sandbox" 绑定。
/// </summary>
public sealed class SandboxOptions
{
    /// <summary>下载 Token 的有效期（分钟），默认 60 分钟。</summary>
    [ConfigurationKeyName("token_expiry_minutes")]
    public int TokenExpiryMinutes { get; set; } = 60;
}
