using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using MicroClaw.Configuration;
using MicroClaw.Endpoints;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Gateway.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Tests.Auth;

[Collection("Config")]
public sealed class AuthEndpointsTests : IDisposable
{
    private const string TestJwtSecret = "this-is-a-test-jwt-secret-key-that-is-long-enough-for-hs256";
    private const string TestUsername = "admin";
    private const string TestPassword = "test-password-123";

    private readonly TestServer _server;
    private readonly HttpClient _client;

    public AuthEndpointsTests()
    {
        TestConfigFixture.EnsureInitialized(new Dictionary<string, string?>
        {
            ["auth:username"] = TestUsername,
            ["auth:password"] = TestPassword,
            ["auth:jwt_secret"] = TestJwtSecret,
            ["auth:expires_hours"] = "8",
        });

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapAuthEndpoints();
                });
            });

        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var request = new LoginRequest(TestUsername, TestPassword);

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.Username.Should().Be(TestUsername);
        result.Role.Should().Be("admin");
        result.ExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var request = new LoginRequest(TestUsername, "wrong-password");

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithWrongUsername_Returns401()
    {
        var request = new LoginRequest("unknown-user", TestPassword);

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyCredentials_ReturnsBadRequest()
    {
        var request = new LoginRequest("", "");

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_JwtContainsExpectedClaims()
    {
        var request = new LoginRequest(TestUsername, TestPassword);
        var response = await _client.PostAsJsonAsync("/auth/login", request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result!.Token);

        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == TestUsername);
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == TestUsername);
        token.Claims.Should().Contain(c =>
            (c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role) && c.Value == "admin");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public async Task Login_UsernameIsCaseInsensitive()
    {
        var request = new LoginRequest("ADMIN", TestPassword);

        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
