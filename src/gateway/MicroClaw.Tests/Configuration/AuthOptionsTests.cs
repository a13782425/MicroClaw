using FluentAssertions;
using MicroClaw.Infrastructure.Configuration;

namespace MicroClaw.Tests.Configuration;

public sealed class AuthOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new AuthOptions();

        options.Username.Should().Be("admin");
        options.Password.Should().BeEmpty();
        options.JwtSecret.Should().BeEmpty();
        options.ExpiresHours.Should().Be(8);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new AuthOptions
        {
            Username = "testuser",
            Password = "testpass",
            JwtSecret = "my-jwt-secret-key-1234",
            ExpiresHours = 24
        };

        options.Username.Should().Be("testuser");
        options.Password.Should().Be("testpass");
        options.JwtSecret.Should().Be("my-jwt-secret-key-1234");
        options.ExpiresHours.Should().Be(24);
    }
}
