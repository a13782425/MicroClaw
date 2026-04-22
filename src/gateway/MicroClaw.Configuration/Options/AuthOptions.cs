
namespace MicroClaw.Configuration;

[MicroClawYamlConfig("auth", FileName = "auth.yaml")]
public sealed class AuthOptions : IMicroClawConfigTemplate
{
    public const string DefaultPassword = "changeme";

    public const string DefaultJwtSecret = "please-change-this-secret-key-min-32-chars!!";

    [YamlMember(Alias = "username")]
    public string Username { get; set; } = "admin";

    [YamlMember(Alias = "password")]
    public string Password { get; set; } = DefaultPassword;

    [YamlMember(Alias = "jwt_secret")]
    public string JwtSecret { get; set; } = DefaultJwtSecret;

    [YamlMember(Alias = "expires_hours")]
    public int ExpiresHours { get; set; } = 8;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new AuthOptions();
}
