using FluentAssertions;
using MicroClaw.Commands;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Commands;

public sealed class ServeCommandTests
{
    [Fact]
    public void EnsureAuthConfigurationIsSafe_WhenUsingDefaultTemplateSecrets_Throws()
    {
        Action action = () => ServeCommand.EnsureAuthConfigurationIsSafe(new AuthOptions());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*默认认证占位值*");
    }

    [Fact]
    public void EnsureAuthConfigurationIsSafe_WhenUsingCustomSecrets_DoesNotThrow()
    {
        AuthOptions options = new()
        {
            Password = "custom-password",
            JwtSecret = "this-is-a-custom-jwt-secret-with-32-chars"
        };

        Action action = () => ServeCommand.EnsureAuthConfigurationIsSafe(options);

        action.Should().NotThrow();
    }

    [Fact]
    public void EnsureAuthConfigurationIsSafe_WhenJwtSecretTooShort_Throws()
    {
        AuthOptions options = new()
        {
            Password = "custom-password",
            JwtSecret = "too-short"
        };

        Action action = () => ServeCommand.EnsureAuthConfigurationIsSafe(options);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT secret 强度不足*");
    }
}