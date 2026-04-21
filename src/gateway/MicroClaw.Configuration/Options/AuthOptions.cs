using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

[MicroClawYamlConfig("auth", FileName = "auth.yaml")]
public sealed class AuthOptions : IMicroClawConfigOptions
{
    [ConfigurationKeyName("username")]
    public string Username { get; set; } = "admin";

    [ConfigurationKeyName("password")]
    public string Password { get; set; } = "";

    [ConfigurationKeyName("jwt_secret")]
    public string JwtSecret { get; set; } = "";

    [ConfigurationKeyName("expires_hours")]
    public int ExpiresHours { get; set; } = 8;
}
