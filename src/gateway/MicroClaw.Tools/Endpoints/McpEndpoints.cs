using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tools.Endpoints;

/// <summary>
/// 全局 MCP Server 管理 REST API 端点：CRUD + 连接测试 + 工具预览。
/// </summary>
public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── CRUD ─────────────────────────────────────────────────────────────

        endpoints.MapGet("/mcp-servers", (McpServerConfigStore store) =>
            Results.Ok(store.All.Select(ToDto)))
            .WithTags("MCP");

        endpoints.MapGet("/mcp-servers/{id}", (string id, McpServerConfigStore store) =>
        {
            McpServerConfig? cfg = store.GetById(id);
            return cfg is null ? Results.NotFound() : Results.Ok(ToDto(cfg));
        })
        .WithTags("MCP");

        endpoints.MapPost("/mcp-servers", (McpServerCreateRequest req, McpServerConfigStore store, IServiceProvider sp) =>
        {
            IMcpServerRegistry? registry = sp.GetService<IMcpServerRegistry>();
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            McpTransportType transport = ParseTransport(req.TransportType);
            if (transport == McpTransportType.Stdio && string.IsNullOrWhiteSpace(req.Command))
                return Results.BadRequest(new { success = false, message = "Command is required for stdio transport.", errorCode = "BAD_REQUEST" });
            if (transport is McpTransportType.Sse or McpTransportType.Http && string.IsNullOrWhiteSpace(req.Url))
                return Results.BadRequest(new { success = false, message = "Url is required for sse/http transport.", errorCode = "BAD_REQUEST" });

            McpServerConfig config = new(
                Name: req.Name.Trim(),
                TransportType: transport,
                Command: req.Command?.Trim(),
                Args: req.Args,
                Env: req.Env,
                Url: req.Url?.Trim(),
                Headers: req.Headers,
                IsEnabled: req.IsEnabled,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            McpServerConfig created = store.Add(config);
            registry?.Register(created);  // 通知运行时注册表，无需重启即可生效
            return Results.Ok(new { created.Id });
        })
        .WithTags("MCP");

        endpoints.MapPost("/mcp-servers/update", (McpServerUpdateRequest req, McpServerConfigStore store, IServiceProvider sp) =>
        {
            IMcpServerRegistry? registry = sp.GetService<IMcpServerRegistry>();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            McpServerConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"MCP server '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            McpServerConfig updated;
            if (existing.Source == McpServerSource.Plugin)
            {
                // 插件 MCP 只允许覆盖 Env 和 Headers 字段
                updated = existing with
                {
                    Env     = req.Env     ?? existing.Env,
                    Headers = req.Headers ?? existing.Headers,
                };
            }
            else
            {
                McpTransportType transport = req.TransportType is not null
                    ? ParseTransport(req.TransportType)
                    : existing.TransportType;

                updated = existing with
                {
                    Name          = req.Name?.Trim()            ?? existing.Name,
                    TransportType = transport,
                    Command       = req.Command?.Trim()          ?? existing.Command,
                    Args          = req.Args                     ?? existing.Args,
                    Env           = req.Env                      ?? existing.Env,
                    Url           = req.Url?.Trim()              ?? existing.Url,
                    Headers       = req.Headers                  ?? existing.Headers,
                    IsEnabled     = req.IsEnabled                ?? existing.IsEnabled,
                };
            }

            McpServerConfig? result = store.Update(req.Id, updated);
            if (result is not null) registry?.Register(result);  // 同步运行时注册表
            return result is null ? Results.NotFound() : Results.Ok(new { result.Id });
        })
        .WithTags("MCP");

        endpoints.MapPost("/mcp-servers/delete", (McpServerDeleteRequest req, McpServerConfigStore store, IServiceProvider sp) =>
        {
            IMcpServerRegistry? registry = sp.GetService<IMcpServerRegistry>();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            McpServerConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"MCP server '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            if (existing.Source == McpServerSource.Plugin)
                return Results.Json(
                    new { success = false, message = $"插件 MCP 不能手动删除，请卸载插件 \u201c{existing.PluginName}\u201d 后自动移除。", errorCode = "FORBIDDEN" },
                    statusCode: 403);

            if (!store.Delete(req.Id))
                return Results.NotFound(new { success = false, message = $"MCP server '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            registry?.Unregister(req.Id);  // 从运行时注册表移除，无需重启即生效
            return Results.Ok();
        })
        .WithTags("MCP");

        // ── 连接测试 ──────────────────────────────────────────────────────────

        endpoints.MapPost("/mcp-servers/{id}/test", async (
            string id,
            McpServerConfigStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            McpServerConfig? cfg = store.GetById(id);
            if (cfg is null)
                return Results.NotFound(new { success = false, message = $"MCP server '{id}' not found.", errorCode = "NOT_FOUND" });

            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var (tools, connections) = await ToolRegistry.LoadToolsAsync([cfg], loggerFactory, cts.Token);
                foreach (IAsyncDisposable conn in connections)
                    await conn.DisposeAsync();

                return Results.Ok(new
                {
                    success = true,
                    toolCount = tools.Count,
                    toolNames = tools.Select(t => t.Name).ToList()
                });
            }
            catch (OperationCanceledException)
            {
                return Results.Ok(new { success = false, error = "Connection timed out (15s)." });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, error = ex.Message });
            }
        })
        .WithTags("MCP");

        // ── 工具预览 ──────────────────────────────────────────────────────────

        endpoints.MapGet("/mcp-servers/{id}/tools", async (
            string id,
            McpServerConfigStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            McpServerConfig? cfg = store.GetById(id);
            if (cfg is null)
                return Results.NotFound(new { success = false, message = $"MCP server '{id}' not found.", errorCode = "NOT_FOUND" });

            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var (tools, connections) = await ToolRegistry.LoadToolsAsync([cfg], loggerFactory, cts.Token);
                var result = tools.Select(t => new
                {
                    name = t.Name,
                    description = t.Description
                }).ToList();

                foreach (IAsyncDisposable conn in connections)
                    await conn.DisposeAsync();

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502);
            }
        })
        .WithTags("MCP");

        return endpoints;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private static object ToDto(McpServerConfig c) => new
    {
        id             = c.Id,
        name           = c.Name,
        transportType  = c.TransportType.ToString().ToLowerInvariant(),
        command        = c.Command,
        args           = c.Args,
        env            = c.Env,
        url            = c.Url,
        headers        = c.Headers,
        isEnabled      = c.IsEnabled,
        createdAtUtc   = c.CreatedAtUtc,
        source         = c.Source.ToString().ToLowerInvariant(),
        pluginId       = c.PluginId,
        pluginName     = c.PluginName,
        requiredEnvVars = EnvVarResolver.ExtractPlaceholders(c)
            .Select(v => new { name = v.Name, isSet = v.IsSet, foundIn = v.FoundIn })
            .ToList(),
    };

    private static McpTransportType ParseTransport(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "sse"  => McpTransportType.Sse,
            "http" => McpTransportType.Http,
            _      => McpTransportType.Stdio,
        };
}

// ── Request 模型 ─────────────────────────────────────────────────────────────

public sealed record McpServerCreateRequest(
    string Name,
    string TransportType = "stdio",
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IDictionary<string, string?>? Env = null,
    string? Url = null,
    IDictionary<string, string>? Headers = null,
    bool IsEnabled = true);

public sealed record McpServerUpdateRequest(
    string Id,
    string? Name = null,
    string? TransportType = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IDictionary<string, string?>? Env = null,
    string? Url = null,
    IDictionary<string, string>? Headers = null,
    bool? IsEnabled = null);

public sealed record McpServerDeleteRequest(string Id);
