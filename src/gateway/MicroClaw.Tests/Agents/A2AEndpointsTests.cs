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
/// A2A 绔偣闆嗘垚娴嬭瘯锛圓gent Card + JSON-RPC 璺敱灞傦級銆?
/// GET /a2a/agent/{id} 鈥?Agent Card锛堜粎闇€ AgentStore锛屾棤闇€ AgentRunner锛夈€?
/// POST 楠岃瘉灞傛祴璇曪紙瑙ｆ瀽閿欒 / 鏂规硶鏈壘鍒?/ Agent 鏈毚闇诧級銆?
/// </summary>
public sealed class A2AEndpointsTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly TestServer _server;
    private readonly HttpClient _client;
    private readonly AgentStore _agentStore;

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public A2AEndpointsTests()
    {
        TestConfigFixture.EnsureInitialized();
        string configDir = _tempDir.Path;
        _agentStore = new AgentStore();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(b => b.ClearProviders());
                services.AddSingleton<AgentStore>(_ => new AgentStore());
            })
            .Configure(app =>
            {
                app.UseRouting();
                // 鍙寕杞?A2A 鐨?GET 绔偣锛圥OST 闇€瑕?AgentRunner锛屽崟鐙祴璇曡姹傝В鏋愬眰锛?
                app.UseEndpoints(e => e.MapGet("/a2a/agent/{agentId}", A2AGetHandler.Handle));
            });

        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        _tempDir.Dispose();
    }

    // 鈹€鈹€ Agent Card GET 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public async Task GetAgentCard_ExposedAgent_ReturnsCard()
    {
        var agent = _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: "MyBot",
            Description: "A helpful bot.",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
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
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
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
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
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
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: true));

        var response = await _client.GetAsync($"/a2a/agent/{agent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>(JsonWeb);
        card.GetProperty("url").GetString().Should().Contain(agent.Id);
    }
}

// 鈹€鈹€ 杞婚噺 GET Handler锛堜粎鐢ㄤ簬娴嬭瘯锛岄伩鍏嶆敞鍏ュ鏉傜殑 AgentRunner锛夆攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// 浠?TestServer DI 涓彁鍙?AgentStore锛屽鐜?A2AEndpoints.MapA2AEndpoints GET 閫昏緫銆?
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
