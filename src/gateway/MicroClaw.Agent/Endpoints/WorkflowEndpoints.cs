using System.Text.Json;
using MicroClaw.Agent.Workflows;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Agent.Endpoints;

/// <summary>
/// 工作流 REST API 端点。
/// GET /api/workflows                  — 列表
/// POST /api/workflows                 — 创建
/// GET /api/workflows/{id}             — 详情
/// PUT /api/workflows/{id}             — 更新
/// DELETE /api/workflows/{id}          — 删除
/// POST /api/workflows/{id}/execute    — 流式执行（SSE）
/// </summary>
public static class WorkflowEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/workflows
        endpoints.MapGet("/workflows", (WorkflowStore store) =>
            Results.Ok(store.All.Select(ToDto)))
            .WithTags("Workflows");

        // GET /api/workflows/{id}
        endpoints.MapGet("/workflows/{id}", (string id, WorkflowStore store) =>
        {
            WorkflowConfig? wf = store.GetById(id);
            return wf is null
                ? Results.NotFound(new { success = false, message = $"Workflow '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(ToDto(wf));
        })
        .WithTags("Workflows");

        // POST /api/workflows
        endpoints.MapPost("/workflows", (WorkflowCreateRequest req, WorkflowStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            WorkflowConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                Description: req.Description ?? string.Empty,
                IsEnabled: req.IsEnabled,
                Nodes: req.Nodes ?? [],
                Edges: req.Edges ?? [],
                EntryNodeId: req.EntryNodeId,
                DefaultProviderId: req.DefaultProviderId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            WorkflowConfig created = store.Add(config);
            return Results.Ok(ToDto(created));
        })
        .WithTags("Workflows");

        // PUT /api/workflows/{id}
        endpoints.MapPut("/workflows/{id}", (string id, WorkflowUpdateRequest req, WorkflowStore store) =>
        {
            WorkflowConfig? existing = store.GetById(id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Workflow '{id}' not found.", errorCode = "NOT_FOUND" });

            WorkflowConfig updated = existing with
            {
                Name = req.Name?.Trim() ?? existing.Name,
                Description = req.Description ?? existing.Description,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                Nodes = req.Nodes ?? existing.Nodes,
                Edges = req.Edges ?? existing.Edges,
                EntryNodeId = req.EntryNodeId ?? existing.EntryNodeId,
                DefaultProviderId = req.DefaultProviderId ?? existing.DefaultProviderId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            WorkflowConfig? result = store.Update(id, updated);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ToDto(result));
        })
        .WithTags("Workflows");

        // DELETE /api/workflows/{id}
        endpoints.MapDelete("/workflows/{id}", (string id, WorkflowStore store) =>
        {
            bool deleted = store.Delete(id);
            return deleted
                ? Results.Ok(new { success = true })
                : Results.NotFound(new { success = false, message = $"Workflow '{id}' not found.", errorCode = "NOT_FOUND" });
        })
        .WithTags("Workflows");

        // POST /api/workflows/{id}/execute — SSE 流式执行
        endpoints.MapPost("/workflows/{id}/execute", async (string id, WorkflowExecuteRequest req, WorkflowStore store, WorkflowEngine engine, HttpContext ctx) =>
        {
            WorkflowConfig? wf = store.GetById(id);
            if (wf is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(
                    new { success = false, message = $"Workflow '{id}' not found.", errorCode = "NOT_FOUND" });
                return;
            }

            if (!wf.IsEnabled)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new { success = false, message = "Workflow is disabled.", errorCode = "WORKFLOW_DISABLED" });
                return;
            }

            if (string.IsNullOrWhiteSpace(req.Input))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    new { success = false, message = "Input is required.", errorCode = "BAD_REQUEST" });
                return;
            }

            string executionId = Guid.NewGuid().ToString("N");
            CancellationToken ct = ctx.RequestAborted;

            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await foreach (StreamItem item in engine.ExecuteAsync(wf, req.Input, executionId, ct))
                {
                    // 不可见于前端的事件跳过 SSE 推送
                    if (!MessageVisibility.IsVisibleToFrontend(item.Visibility))
                        continue;

                    await WriteSseAsync(ctx.Response, StreamItemSerializer.Serialize(item), ct);
                }

                string doneData = JsonSerializer.Serialize(new { type = "done" }, JsonOpts);
                await WriteSseAsync(ctx.Response, doneData, ct);
                await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
            }
            catch (OperationCanceledException)
            {
                // 客户端断开，静默处理
            }
            catch (Exception ex)
            {
                try
                {
                    string errData = JsonSerializer.Serialize(new { type = "error", message = ex.Message }, JsonOpts);
                    await WriteSseAsync(ctx.Response, errData, CancellationToken.None);
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch
                {
                    // 响应已关闭，忽略
                }
            }
        })
        .WithTags("Workflows");

        return endpoints;
    }

    // ── DTO 映射 ──────────────────────────────────────────────────────────────

    private static object ToDto(WorkflowConfig wf) => new
    {
        wf.Id,
        wf.Name,
        wf.Description,
        wf.IsEnabled,
        wf.Nodes,
        wf.Edges,
        wf.EntryNodeId,
        wf.DefaultProviderId,
        CreatedAt = wf.CreatedAtUtc.ToString("o"),
        UpdatedAt = wf.UpdatedAtUtc.ToString("o")
    };

    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

}

// ── 请求模型 ──────────────────────────────────────────────────────────────────

public sealed record WorkflowCreateRequest(
    string Name,
    string? Description,
    bool IsEnabled,
    IReadOnlyList<WorkflowNodeConfig>? Nodes,
    IReadOnlyList<WorkflowEdgeConfig>? Edges,
    string? EntryNodeId,
    string? DefaultProviderId = null);

public sealed record WorkflowUpdateRequest(
    string? Name,
    string? Description,
    bool? IsEnabled,
    IReadOnlyList<WorkflowNodeConfig>? Nodes,
    IReadOnlyList<WorkflowEdgeConfig>? Edges,
    string? EntryNodeId,
    string? DefaultProviderId = null);

public sealed record WorkflowExecuteRequest(string Input);
