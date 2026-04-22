using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;

namespace MicroClaw.Tests.Core;

public sealed class MicroClawConfigurationExtensionsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-config-ext-tests", Guid.NewGuid().ToString("N"));
    private readonly string? _originalPassword = Environment.GetEnvironmentVariable("auth__password");
    private readonly string? _originalDotnetPassword = Environment.GetEnvironmentVariable("DOTNET_auth__password");

    [Fact]
    public void EnvironmentVariablesAddedAfterYaml_ShouldOverrideYamlValues()
    {
        string configPath = CreateConfigFile("""
            auth:
              password: yaml-password
            """);
        Environment.SetEnvironmentVariable("auth__password", "env-password");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddMicroClawYaml(configPath)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables("DOTNET_")
            .Build();

        configuration["auth:password"].Should().Be("env-password");
    }

    [Fact]
    public void DotNetPrefixedEnvironmentVariablesAddedAfterYaml_ShouldOverrideYamlValues()
    {
        string configPath = CreateConfigFile("""
            auth:
              password: yaml-password
            """);
        Environment.SetEnvironmentVariable("DOTNET_auth__password", "dotnet-password");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddMicroClawYaml(configPath)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables("DOTNET_")
            .Build();

        configuration["auth:password"].Should().Be("dotnet-password");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("auth__password", _originalPassword);
        Environment.SetEnvironmentVariable("DOTNET_auth__password", _originalDotnetPassword);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string CreateConfigFile(string content)
    {
        Directory.CreateDirectory(_tempRoot);
        string path = Path.Combine(_tempRoot, "microclaw.yaml");
        File.WriteAllText(path, content);
        return path;
    }
}