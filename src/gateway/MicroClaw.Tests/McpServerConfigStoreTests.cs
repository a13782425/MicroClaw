using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Tools;
using MicroClaw.Utils;

namespace MicroClaw.Tests;

public sealed class McpServerConfigStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-mcp-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configDir;

    public McpServerConfigStoreTests()
    {
        _configDir = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);
        MicroClawConfig.Reset();
    }

    [Fact]
    public void AllEnabled_WhenConfigured_ReturnsOnlyEnabledServersOrderedByCreatedAt()
    {
        InitializeConfig(
        [
            new McpServerConfigEntity
            {
                Id = "b",
                Name = "Disabled",
                TransportType = "stdio",
                IsEnabled = false,
                CreatedAtMs = 200,
            },
            new McpServerConfigEntity
            {
                Id = "a",
                Name = "Enabled",
                TransportType = "http",
                Url = "https://example.com/mcp",
                IsEnabled = true,
                CreatedAtMs = 100,
            }
        ]);

        McpServerConfigStore store = new();

        store.AllEnabled.Should().ContainSingle();
        store.AllEnabled[0].Id.Should().Be("a");
        store.AllEnabled[0].TransportType.Should().Be(McpTransportType.Http);
    }

    [Fact]
    public void Add_WhenCalled_PersistsServerIntoConfigAndYaml()
    {
        InitializeConfig([]);
        McpServerConfigStore store = new();

        McpServerConfig created = store.Add(new McpServerConfig(
            Name: "Filesystem",
            TransportType: McpTransportType.Stdio,
            Command: "npx",
            Args: ["-y", "@modelcontextprotocol/server-filesystem"],
            Env: new Dictionary<string, string?> { ["ROOT"] = "/workspace" }));

        McpServersOptions options = MicroClawConfig.Get<McpServersOptions>();
        options.Items.Should().ContainSingle(item => item.Id == created.Id && item.Name == "Filesystem");

        string yaml = File.ReadAllText(Path.Combine(_configDir, "mcp-servers.yaml"));
        yaml.Should().Contain("mcp_servers:");
        yaml.Should().Contain("items:");
        yaml.Should().Contain($"id: {created.Id}");
    }

    [Fact]
    public void Upsert_WhenExisting_PreservesCreatedAtAndUpdatesMetadata()
    {
        InitializeConfig(
        [
            new McpServerConfigEntity
            {
                Id = "srv-1",
                Name = "Old",
                TransportType = "stdio",
                Command = "node",
                IsEnabled = true,
                CreatedAtMs = 123,
                Source = (int)McpServerSource.Manual,
            }
        ]);

        McpServerConfigStore store = new();

        McpServerConfig updated = store.Upsert(new McpServerConfig(
            Id: "srv-1",
            Name: "Updated",
            TransportType: McpTransportType.Sse,
            Url: "https://example.com/sse",
            IsEnabled: false,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Source: McpServerSource.Plugin,
            PluginId: "plugin-a",
            PluginName: "Plugin A"));

        updated.Name.Should().Be("Updated");
        updated.TransportType.Should().Be(McpTransportType.Sse);
        updated.CreatedAtUtc.Should().Be(TimeUtils.FromMs(123));
        updated.Source.Should().Be(McpServerSource.Plugin);
        updated.PluginId.Should().Be("plugin-a");

        MicroClawConfig.Get<McpServersOptions>().Items.Should().ContainSingle(item =>
            item.Id == "srv-1" && item.CreatedAtMs == 123 && item.PluginId == "plugin-a");
    }

    [Fact]
    public void DeleteByPluginId_WhenMatchesExist_RemovesAndReturnsCount()
    {
        InitializeConfig(
        [
            new McpServerConfigEntity
            {
                Id = "srv-1",
                Name = "Plugin Server",
                TransportType = "stdio",
                PluginId = "plugin-a",
                CreatedAtMs = 100,
            },
            new McpServerConfigEntity
            {
                Id = "srv-2",
                Name = "Manual Server",
                TransportType = "stdio",
                PluginId = null,
                CreatedAtMs = 200,
            }
        ]);

        McpServerConfigStore store = new();

        int deleted = store.DeleteByPluginId("plugin-a");

        deleted.Should().Be(1);
        MicroClawConfig.Get<McpServersOptions>().Items.Should().ContainSingle(item => item.Id == "srv-2");
    }

    public void Dispose()
    {
        MicroClawConfig.Reset();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private void InitializeConfig(McpServerConfigEntity[] servers)
    {
        Dictionary<string, string?> data = new()
        {
            ["mcp_servers:items"] = null,
        };

        for (int i = 0; i < servers.Length; i++)
        {
            McpServerConfigEntity server = servers[i];
            data[$"mcp_servers:items:{i}:id"] = server.Id;
            data[$"mcp_servers:items:{i}:name"] = server.Name;
            data[$"mcp_servers:items:{i}:transport_type"] = server.TransportType;
            data[$"mcp_servers:items:{i}:command"] = server.Command;
            data[$"mcp_servers:items:{i}:args_json"] = server.ArgsJson;
            data[$"mcp_servers:items:{i}:env_json"] = server.EnvJson;
            data[$"mcp_servers:items:{i}:url"] = server.Url;
            data[$"mcp_servers:items:{i}:headers_json"] = server.HeadersJson;
            data[$"mcp_servers:items:{i}:is_enabled"] = server.IsEnabled.ToString();
            data[$"mcp_servers:items:{i}:created_at_ms"] = server.CreatedAtMs.ToString();
            data[$"mcp_servers:items:{i}:source"] = server.Source.ToString();
            data[$"mcp_servers:items:{i}:plugin_id"] = server.PluginId;
            data[$"mcp_servers:items:{i}:plugin_name"] = server.PluginName;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);
    }
}