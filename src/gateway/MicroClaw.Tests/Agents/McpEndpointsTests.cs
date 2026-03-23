using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using MicroClaw.Tools.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// McpEndpoints HTTP 层集成测试（使用 In-Memory SQLite + TestServer）。
/// 跳过需要真实进程的 /test 和 /tools 端点，只测试 CRUD 逻辑。
/// </summary>
public sealed class McpEndpointsTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly TestServer _server;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public McpEndpointsTests()
    {
        var factory = _db.CreateFactory();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<McpServerConfigStore>(_ => new McpServerConfigStore(factory));
                services.AddLogging(b => b.ClearProviders());
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapMcpEndpoints());
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

    // ── GET /mcp-servers ─────────────────────────────────────────────────────

    [Fact]
    public async Task List_WhenEmpty_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/mcp-servers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(_json);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task List_AfterCreate_ReturnsSingleItem()
    {
        await CreateStdioServer("myserver");

        var response = await _client.GetAsync("/mcp-servers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(_json);
        body.Should().HaveCount(1);
        body![0].GetProperty("name").GetString().Should().Be("myserver");
        body[0].GetProperty("transportType").GetString().Should().Be("stdio");
    }

    // ── GET /mcp-servers/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingId_ReturnsServer()
    {
        var id = await CreateStdioServer("srv1");

        var response = await _client.GetAsync($"/mcp-servers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("id").GetString().Should().Be(id);
        body.GetProperty("name").GetString().Should().Be("srv1");
    }

    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/mcp-servers/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /mcp-servers (create) ────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidStdio_ReturnsId()
    {
        var req = new McpServerCreateRequest(
            Name: "fs-server",
            TransportType: "stdio",
            Command: "npx",
            Args: ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]);

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Create_ValidSse_ReturnsId()
    {
        var req = new McpServerCreateRequest(
            Name: "remote-sse",
            TransportType: "sse",
            Url: "http://localhost:3000/sse");

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var req = new McpServerCreateRequest(Name: "   ", TransportType: "stdio", Command: "npx");

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_StdioMissingCommand_Returns400()
    {
        var req = new McpServerCreateRequest(Name: "no-cmd", TransportType: "stdio");

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_SseMissingUrl_Returns400()
    {
        var req = new McpServerCreateRequest(Name: "no-url", TransportType: "sse");

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ServerAppearsInList()
    {
        await CreateStdioServer("appear-test");

        var response = await _client.GetAsync("/mcp-servers");
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(_json);

        body.Should().ContainSingle(x => x.GetProperty("name").GetString() == "appear-test");
    }

    // ── POST /mcp-servers/update ──────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingServer_ChangesName()
    {
        var id = await CreateStdioServer("old-name");

        var req = new McpServerUpdateRequest(Id: id, Name: "new-name");
        var response = await _client.PostAsJsonAsync("/mcp-servers/update", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await _client.GetAsync($"/mcp-servers/{id}");
        var body = await get.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("name").GetString().Should().Be("new-name");
    }

    [Fact]
    public async Task Update_NonExistentId_Returns404()
    {
        var req = new McpServerUpdateRequest(Id: "ghost-id", Name: "x");

        var response = await _client.PostAsJsonAsync("/mcp-servers/update", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_MissingId_Returns400()
    {
        var req = new McpServerUpdateRequest(Id: "", Name: "x");

        var response = await _client.PostAsJsonAsync("/mcp-servers/update", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ToggleIsEnabled_PersistsChange()
    {
        var id = await CreateStdioServer("toggle-test");

        var req = new McpServerUpdateRequest(Id: id, IsEnabled: false);
        await _client.PostAsJsonAsync("/mcp-servers/update", req);

        var get = await _client.GetAsync($"/mcp-servers/{id}");
        var body = await get.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }

    // ── POST /mcp-servers/delete ──────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingServer_Returns200()
    {
        var id = await CreateStdioServer("to-delete");

        var req = new McpServerDeleteRequest(id);
        var response = await _client.PostAsJsonAsync("/mcp-servers/delete", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ExistingServer_RemovesFromList()
    {
        var id = await CreateStdioServer("soon-gone");

        await _client.PostAsJsonAsync("/mcp-servers/delete", new McpServerDeleteRequest(id));

        var list = await _client.GetFromJsonAsync<JsonElement[]>("/mcp-servers", _json);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NonExistentId_Returns404()
    {
        var req = new McpServerDeleteRequest("ghost");

        var response = await _client.PostAsJsonAsync("/mcp-servers/delete", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_MissingId_Returns400()
    {
        var req = new McpServerDeleteRequest("");

        var response = await _client.PostAsJsonAsync("/mcp-servers/delete", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private async Task<string> CreateStdioServer(string name)
    {
        var req = new McpServerCreateRequest(
            Name: name,
            TransportType: "stdio",
            Command: "echo",
            Args: ["hello"]);

        var response = await _client.PostAsJsonAsync("/mcp-servers", req);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        return body.GetProperty("id").GetString()!;
    }
}
