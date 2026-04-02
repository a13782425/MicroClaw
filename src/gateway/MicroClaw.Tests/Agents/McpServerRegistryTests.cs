using FluentAssertions;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using MicroClaw.Tools.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// IMcpServerRegistry / McpServerRegistry 单元测试 + McpEndpoints registry 通知集成测试。
/// </summary>
public sealed class McpServerRegistryTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly McpServerConfigStore _store;

    public McpServerRegistryTests()
    {
        _store = new McpServerConfigStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── 辅助工厂 ─────────────────────────────────────────────────────────────

    private static McpServerConfig Stdio(string name = "fs", string id = "", bool isEnabled = true,
        DateTimeOffset createdAt = default) =>
        new(
            Name: name,
            TransportType: McpTransportType.Stdio,
            Command: "npx",
            Args: ["-y", "@modelcontextprotocol/server-filesystem"],
            IsEnabled: isEnabled,
            Id: id,
            CreatedAtUtc: createdAt == default ? DateTimeOffset.UtcNow : createdAt);

    private McpServerRegistry BuildRegistry() =>
        new(_store, NullLogger<McpServerRegistry>.Instance);

    // ── GetAll / GetAllEnabled / GetById（空注册表）────────────────────────────

    [Fact]
    public void GetAll_WhenEmpty_ReturnsEmptyList()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAllEnabled_WhenEmpty_ReturnsEmptyList()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.GetAllEnabled().Should().BeEmpty();
    }

    [Fact]
    public void GetById_WhenEmpty_ReturnsNull()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.GetById("nonexistent").Should().BeNull();
    }

    // ── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public void Register_AddsServer_CanBeRetrievedByGetAll()
    {
        McpServerRegistry reg = BuildRegistry();
        McpServerConfig cfg = Stdio(id: "id1");

        reg.Register(cfg);

        reg.GetAll().Should().ContainSingle(s => s.Id == "id1");
    }

    [Fact]
    public void Register_SameId_UpdatesExistingEntry()
    {
        McpServerRegistry reg = BuildRegistry();
        McpServerConfig original = Stdio(name: "original", id: "id1");
        McpServerConfig updated  = Stdio(name: "updated",  id: "id1");

        reg.Register(original);
        reg.Register(updated);

        reg.GetAll().Should().ContainSingle();
        reg.GetById("id1")!.Name.Should().Be("updated");
    }

    [Fact]
    public void Register_MultipleServers_AllRetrievable()
    {
        McpServerRegistry reg = BuildRegistry();

        reg.Register(Stdio(name: "a", id: "1"));
        reg.Register(Stdio(name: "b", id: "2"));
        reg.Register(Stdio(name: "c", id: "3"));

        reg.GetAll().Should().HaveCount(3);
    }

    // ── Unregister ───────────────────────────────────────────────────────────

    [Fact]
    public void Unregister_ExistingServer_RemovesFromRegistry()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(id: "id1"));

        reg.Unregister("id1");

        reg.GetAll().Should().BeEmpty();
        reg.GetById("id1").Should().BeNull();
    }

    [Fact]
    public void Unregister_NonExistentId_DoesNotThrow()
    {
        McpServerRegistry reg = BuildRegistry();

        Action act = () => reg.Unregister("ghost");
        act.Should().NotThrow();
    }

    [Fact]
    public void Unregister_OneOfMany_OnlyRemovesTarget()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(id: "a"));
        reg.Register(Stdio(id: "b"));
        reg.Register(Stdio(id: "c"));

        reg.Unregister("b");

        var all = reg.GetAll().Select(s => s.Id);
        all.Should().BeEquivalentTo(["a", "c"]);
    }

    // ── GetAllEnabled ────────────────────────────────────────────────────────

    [Fact]
    public void GetAllEnabled_FiltersDisabledServers()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(name: "active", id: "1", isEnabled: true));
        reg.Register(Stdio(name: "inactive", id: "2", isEnabled: false));

        IReadOnlyList<McpServerConfig> enabled = reg.GetAllEnabled();

        enabled.Should().ContainSingle(s => s.Name == "active");
        enabled.Should().NotContain(s => s.Name == "inactive");
    }

    [Fact]
    public void GetAllEnabled_WhenDisabledThenReEnabled_ReflectsCurrentState()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(id: "id1", isEnabled: false));
        reg.GetAllEnabled().Should().BeEmpty();

        // 更新为启用
        reg.Register(Stdio(id: "id1", isEnabled: true));
        reg.GetAllEnabled().Should().ContainSingle();
    }

    // ── GetById ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetById_ExistingId_ReturnsCorrectConfig()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(name: "target", id: "id-x"));

        McpServerConfig? found = reg.GetById("id-x");

        found.Should().NotBeNull();
        found!.Name.Should().Be("target");
    }

    [Fact]
    public void GetById_MissingId_ReturnsNull()
    {
        McpServerRegistry reg = BuildRegistry();
        reg.Register(Stdio(id: "id1"));

        reg.GetById("id-missing").Should().BeNull();
    }

    // ── GetAll 排序 ───────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsOrderedByCreatedAtUtc()
    {
        McpServerRegistry reg = BuildRegistry();
        DateTimeOffset base_ = DateTimeOffset.UtcNow;

        reg.Register(Stdio(name: "third",  id: "3", createdAt: base_.AddSeconds(2)));
        reg.Register(Stdio(name: "first",  id: "1", createdAt: base_));
        reg.Register(Stdio(name: "second", id: "2", createdAt: base_.AddSeconds(1)));

        var names = reg.GetAll().Select(s => s.Name).ToList();
        names.Should().ContainInOrder("first", "second", "third");
    }

    // ── ExecuteAsync (启动时从 Store 同步) ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LoadsAllConfigsFromStore()
    {
        // 向 store 预插入两条记录
        _store.Add(Stdio(name: "server-a"));
        _store.Add(Stdio(name: "server-b", isEnabled: false));

        McpServerRegistry reg = BuildRegistry();
        await reg.StartAsync(CancellationToken.None);

        reg.GetAll().Should().HaveCount(2);
        reg.GetAllEnabled().Should().ContainSingle(s => s.Name == "server-a");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyStore_RegistryStartsEmpty()
    {
        McpServerRegistry reg = BuildRegistry();
        await reg.StartAsync(CancellationToken.None);

        reg.GetAll().Should().BeEmpty();
    }

    // ── McpEndpoints 与 Registry 联动集成测试 ─────────────────────────────────

    private TestServer BuildTestServer(IMcpServerRegistry? registry = null)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<McpServerConfigStore>(_ => _store);
                if (registry is not null)
                    services.AddSingleton(registry);
                services.AddLogging(b => b.ClearProviders());
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapMcpEndpoints());
            });

        return new TestServer(builder);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Create_NotifiesRegistry_ServerAvailableWithoutDbQuery()
    {
        var registry = BuildRegistry();
        using TestServer server = BuildTestServer(registry);
        using HttpClient client = server.CreateClient();

        await client.PostAsJsonAsync("/mcp-servers", new
        {
            name = "dynamic-server",
            transportType = "stdio",
            command = "node",
        });

        // 注册表中立即可见，无需重启
        registry.GetAll().Should().ContainSingle(s => s.Name == "dynamic-server");
    }

    [Fact]
    public async Task Update_NotifiesRegistry_ChangesReflectedImmediately()
    {
        var registry = BuildRegistry();
        using TestServer server = BuildTestServer(registry);
        using HttpClient client = server.CreateClient();

        // 创建初始服务器
        var createResp = await client.PostAsJsonAsync("/mcp-servers", new
        {
            name = "original-name",
            transportType = "stdio",
            command = "npx",
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        string id = created.GetProperty("id").GetString()!;

        // 更新名称
        await client.PostAsJsonAsync("/mcp-servers/update", new { id, name = "updated-name" });

        // 注册表立即反映新名称
        registry.GetById(id)!.Name.Should().Be("updated-name");
    }

    [Fact]
    public async Task Delete_NotifiesRegistry_ServerRemovedImmediately()
    {
        var registry = BuildRegistry();
        using TestServer server = BuildTestServer(registry);
        using HttpClient client = server.CreateClient();

        var createResp = await client.PostAsJsonAsync("/mcp-servers", new
        {
            name = "to-delete",
            transportType = "stdio",
            command = "npx",
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        string id = created.GetProperty("id").GetString()!;

        registry.GetAll().Should().ContainSingle(s => s.Id == id);

        await client.PostAsJsonAsync("/mcp-servers/delete", new { id });

        // 注册表立即移除
        registry.GetById(id).Should().BeNull();
        registry.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithoutRegistry_StillWorks()
    {
        // 验证不注册 IMcpServerRegistry 时端点正常工作（向后兼容）
        using TestServer server = BuildTestServer(registry: null);
        using HttpClient client = server.CreateClient();

        var resp = await client.PostAsJsonAsync("/mcp-servers", new
        {
            name = "compat-server",
            transportType = "stdio",
            command = "node",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegisterFromConfigFileAsync_LegacyRootFormat_RegistersServer()
    {
        McpServerRegistry reg = BuildRegistry();
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
            {
              "github": {
                "type": "http",
                "url": "https://api.githubcopilot.com/mcp/",
                "headers": {
                  "Authorization": "Bearer ${GITHUB_PERSONAL_ACCESS_TOKEN}"
                }
              }
            }
            """);

            await reg.RegisterFromConfigFileAsync(tempFile, "github-plugin");

            McpServerConfig? config = reg.GetById("plugin:github-plugin:github");
            config.Should().NotBeNull();
            config!.TransportType.Should().Be(McpTransportType.Http);
            config.Url.Should().Be("https://api.githubcopilot.com/mcp/");
            config.Headers.Should().ContainKey("Authorization")
                .WhoseValue.Should().Be("Bearer ${GITHUB_PERSONAL_ACCESS_TOKEN}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RegisterFromConfigFileAsync_MetadataRootWithoutServerFields_IsIgnored()
    {
        McpServerRegistry reg = BuildRegistry();
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
            {
              "metadata": {
                "name": "github"
              }
            }
            """);

            await reg.RegisterFromConfigFileAsync(tempFile, "github-plugin");

            reg.GetAll().Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TestEndpoint_WhenEnvVariableMissing_ReturnsHelpfulError()
    {
        string? originalValue = Environment.GetEnvironmentVariable("MCP_TEST_TOKEN");
        Environment.SetEnvironmentVariable("MCP_TEST_TOKEN", null);

        try
        {
            var registry = BuildRegistry();
            using TestServer server = BuildTestServer(registry);
            using HttpClient client = server.CreateClient();

            var createResp = await client.PostAsJsonAsync("/mcp-servers", new
            {
                name = "http-env-server",
                transportType = "http",
                url = "https://api.githubcopilot.com/mcp/",
                headers = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer ${MCP_TEST_TOKEN}",
                }
            });
            var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            string id = created.GetProperty("id").GetString()!;

            var testResp = await client.PostAsync($"/mcp-servers/{id}/test", null);
            JsonElement payload = await testResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            payload.GetProperty("success").GetBoolean().Should().BeFalse();
            payload.GetProperty("error").GetString().Should().Contain("MCP_TEST_TOKEN (headers.Authorization)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_TEST_TOKEN", originalValue);
        }
    }

    // ── ToolCollector 优先使用 Registry ──────────────────────────────────────

    [Fact]
    public async Task GetToolGroupsAsync_WithRegistry_UsesRegistryNotStore()
    {
        var mockRegistry = Substitute.For<IMcpServerRegistry>();
        mockRegistry.GetAll().Returns([Stdio(name: "registry-server", id: "r1")]);

        // store 中无任何 MCP Server — 若 collector 查 store，则 GetAll() 会返回空
        var collector = new MicroClaw.Agent.ToolCollector(
            [],
            _store,
            NullLoggerFactory.Instance,
            mockRegistry);

        IReadOnlyList<MicroClaw.Agent.ToolGroupInfo> groups =
            await collector.GetToolGroupsAsync(agent: null, CancellationToken.None);

        groups.Should().Contain(g => g.Id == "r1" && g.Name == "registry-server");
        mockRegistry.Received(1).GetAll();
    }

    [Fact]
    public async Task GetToolGroupsAsync_WithoutRegistry_FallsBackToStore()
    {
        _store.Add(Stdio(name: "store-server"));

        // 不传 registry（null 默认值）
        var collector = new MicroClaw.Agent.ToolCollector([], _store, NullLoggerFactory.Instance);

        IReadOnlyList<MicroClaw.Agent.ToolGroupInfo> groups =
            await collector.GetToolGroupsAsync(agent: null, CancellationToken.None);

        groups.Should().Contain(g => g.Name == "store-server");
    }

    [Fact]
    public async Task CollectToolsAsync_EnabledServers_UsesRegistryGetAllEnabled()
    {
        var mockRegistry = Substitute.For<IMcpServerRegistry>();
        // Registry 返回空（无服务器），store 中有一个 — 若使用 registry，结果不包含 store 中的
        mockRegistry.GetAllEnabled().Returns([]);

        var collector = new MicroClaw.Agent.ToolCollector(
            [],
            _store,
            NullLoggerFactory.Instance,
            mockRegistry);

        MicroClaw.Agent.AgentConfig agent = new(
            Id: "agent1", Name: "test", Description: "", IsEnabled: true,
            DisabledSkillIds: [], DisabledMcpServerIds: [], ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);

        // 使用 registry（空列表），不会连接到任何 MCP Server（无副作用）
        await using MicroClaw.Tools.ToolCollectionResult result =
            await collector.CollectToolsAsync(agent, new MicroClaw.Tools.ToolCreationContext());

        result.AllTools.Should().BeEmpty();
        mockRegistry.Received(1).GetAllEnabled();
    }
}
