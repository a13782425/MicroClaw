using FluentAssertions;
using MicroClaw.Tools;

namespace MicroClaw.Tests.Agents;

public sealed class McpConfigurationResolverTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = [];

    public void Dispose()
    {
        foreach ((string key, string? value) in _originalValues)
            Environment.SetEnvironmentVariable(key, value);
    }

    [Fact]
    public void ResolveEnvironmentVariables_ExpandsUrlHeadersArgsAndEnv()
    {
        RememberEnv("MCP_API_TOKEN");
        RememberEnv("MCP_HOST");
        RememberEnv("MCP_ARGS_VALUE");
        RememberEnv("MCP_CHILD_TOKEN");

        Environment.SetEnvironmentVariable("MCP_API_TOKEN", "token-123");
        Environment.SetEnvironmentVariable("MCP_HOST", "example.com");
        Environment.SetEnvironmentVariable("MCP_ARGS_VALUE", "expanded-arg");
        Environment.SetEnvironmentVariable("MCP_CHILD_TOKEN", "child-secret");

        McpServerConfig config = new(
            Name: "github",
            TransportType: McpTransportType.Http,
            Args: ["--token=${MCP_API_TOKEN}", "--flag", "${MCP_ARGS_VALUE}"],
            Env: new Dictionary<string, string?>
            {
                ["TOKEN"] = "${MCP_CHILD_TOKEN}",
                ["PLAIN"] = "keep-me",
            },
            Url: "https://${MCP_HOST}/mcp",
            Headers: new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer ${MCP_API_TOKEN}",
            });

        McpServerConfig resolved = McpConfigurationResolver.ResolveEnvironmentVariables(config);

        resolved.Url.Should().Be("https://example.com/mcp");
        resolved.Args.Should().Equal("--token=token-123", "--flag", "expanded-arg");
        resolved.Env.Should().ContainKey("TOKEN").WhoseValue.Should().Be("child-secret");
        resolved.Env.Should().ContainKey("PLAIN").WhoseValue.Should().Be("keep-me");
        resolved.Headers.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer token-123");
    }

    [Fact]
    public void ResolveEnvironmentVariables_WhenVariableMissing_ThrowsHelpfulError()
    {
        RememberEnv("MCP_MISSING_TOKEN");
        Environment.SetEnvironmentVariable("MCP_MISSING_TOKEN", null);

        McpServerConfig config = new(
            Name: "github",
            TransportType: McpTransportType.Http,
            Url: "https://example.com/mcp",
            Headers: new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer ${MCP_MISSING_TOKEN}",
            });

        Action act = () => McpConfigurationResolver.ResolveEnvironmentVariables(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MCP_MISSING_TOKEN (headers.Authorization)*");
    }

    private void RememberEnv(string key)
    {
        if (!_originalValues.ContainsKey(key))
            _originalValues[key] = Environment.GetEnvironmentVariable(key);
    }
}