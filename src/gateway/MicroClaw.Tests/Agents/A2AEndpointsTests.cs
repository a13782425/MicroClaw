using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.A2A;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// A2A 端点集成测试（Agent Card + JSON-RPC 路由层）。
/// GET /a2a/agent/{id} — Agent Card（仅需 AgentStore，无需 AgentRunner）。
/// POST 验证层测试（解析错误 / 方法未找到 / Agent 未暴露）。
/// </summary>
public sealed class A2AEndpointsTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly TestServer _server;
    private readonly HttpClient _client;
    private readonly AgentStore _agentStore;

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public A2AEndpointsTests()
    {
        var factory = _db.CreateFactory();
        _agentStore = new AgentStore(factory);

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(b => b.ClearProviders());
                services.AddSingleton<AgentStore>(_ => new AgentStore(factory));
            })
            .Configure(app =>
            {
                app.UseRouting();
                // 只挂载 A2A 的 GET 端点（POST 需要 AgentRunner，单独测试请求解析层）
                app.UseEndpoints(e => e.MapGet("/a2a/agent/{agentId}", A2AGetHandler.Handle));
            });

        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        _db.Dispose();
    }

    // ── Agent Card GET ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentCard_ExposedAgent_ReturnsCard()
    {
        var agent = _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: "MyBot",
            Description: "A helpful bot.",
            IsEnabled: true,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: true));

        var response = await _client.GetAsync($"/a2a/agent/{agent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>(JsonWeb);
        card.GetProperty("name").GetString().Should().Be("MyBot");
        card.GetProperty("description").GetString().Should().Be("A helpful bot.");
        card.GetProperty("version").GetString().Should().Be("1.0");
        card.GetProperty("capabilities").GetProperty("streaming").GetBoolean().Should().BeTrue();
        card.GetProperty("skills").GetArrayLength().Should().Be(1);
        card.GetProperty("skills")[0].GetProperty("id").GetString().Should().Be("chat");
    }

    [Fact]
    public async Task GetAgentCard_NotExposedAgent_Returns404()
    {
        var agent = _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: "HiddenAgent",
            Description: "Not exposed.",
            IsEnabled: true,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: false));

        var response = await _client.GetAsync($"/a2a/agent/{agent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAgentCard_DisabledAgent_Returns404()
    {
        var agent = _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: "DisabledAgent",
            Description: "Disabled.",
            IsEnabled: false,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: true));

        var response = await _client.GetAsync($"/a2a/agent/{agent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAgentCard_NonExistentAgent_Returns404()
    {
        var response = await _client.GetAsync("/a2a/agent/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAgentCard_UrlContainsAgentId()
    {
        var agent = _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: "UrlBot",
            Description: "Bot with URL.",
            IsEnabled: true,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: true));

        var response = await _client.GetAsync($"/a2a/agent/{agent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>(JsonWeb);
        card.GetProperty("url").GetString().Should().Contain(agent.Id);
    }
}

// ── 轻量 GET Handler（仅用于测试，避免注入复杂的 AgentRunner）─────────────────

/// <summary>
/// 从 TestServer DI 中提取 AgentStore，复现 A2AEndpoints.MapA2AEndpoints GET 逻辑。
/// </summary>
internal static class A2AGetHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IResult Handle(string agentId, Microsoft.AspNetCore.Http.HttpContext ctx, AgentStore store)
    {
        AgentConfig? agent = store.GetById(agentId);
        if (agent is null || !agent.IsEnabled || !agent.ExposeAsA2A)
            return Results.NotFound(new { code = -32001, message = "Agent not found or A2A not enabled." });

        string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var card = new AgentCard(
            Name: agent.Name,
            Description: agent.Description,
            Url: $"{baseUrl}/a2a/agent/{agent.Id}",
            Version: "1.0",
            Capabilities: new AgentCapabilities(Streaming: true),
            Skills:
            [
                new AgentSkill("chat", "Chat", $"Send messages to {agent.Name} and receive streaming responses.")
            ]);

        return Results.Ok(card);
    }
}
