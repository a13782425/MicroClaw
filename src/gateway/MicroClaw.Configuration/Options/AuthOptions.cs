
namespace MicroClaw.Configuration;

/// <summary>
/// 认证配置选项，控制默认用户名、登录密码、JWT 密钥和过期时间。
/// </summary>
[MicroClawYamlConfig("auth", FileName = "auth.yaml")]
public sealed class AuthOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 默认密码占位值，用于初始化模板并在启动时触发安全检查。
    /// </summary>
    public const string DefaultPassword = "changeme";

    /// <summary>
    /// 默认 JWT 密钥占位值，用于初始化模板并在启动时触发安全检查。
    /// </summary>
    public const string DefaultJwtSecret = "please-change-this-secret-key-min-32-chars!!";

    /// <summary>
    /// 后台登录用户名，默认值为 admin。
    /// </summary>
    [YamlMember(Alias = "username", Description = "后台登录用户名，默认值为 admin。")]
    public string Username { get; set; } = "admin";

    /// <summary>
    /// 后台登录密码。初始化模板会写入默认占位值，正式环境必须修改。
    /// </summary>
    [YamlMember(Alias = "password", Description = "后台登录密码。初始化模板会写入默认占位值，正式环境必须修改。")]
    public string Password { get; set; } = DefaultPassword;

    /// <summary>
    /// JWT 签名密钥。初始化模板会写入默认占位值，正式环境必须替换为强密钥。
    /// </summary>
    [YamlMember(Alias = "jwt_secret", Description = "JWT 签名密钥。初始化模板会写入默认占位值，正式环境必须替换为强密钥。")]
    public string JwtSecret { get; set; } = DefaultJwtSecret;

    /// <summary>
    /// 访问令牌有效期，单位为小时。
    /// </summary>
    [YamlMember(Alias = "expires_hours", Description = "访问令牌有效期，单位为小时。")]
    public int ExpiresHours { get; set; } = 8;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new AuthOptions();
}
