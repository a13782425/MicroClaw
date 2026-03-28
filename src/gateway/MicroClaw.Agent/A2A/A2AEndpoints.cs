using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.A2A;

/// <summary>
/// A2A（Agent-to-Agent）协议端点。
/// 实现 A2A v0.2 规范（https://google.github.io/A2A）的 JSON-RPC 接口。
/// 
/// GET  /a2a/agent/{agentId}  → Agent Card（发现端点，公开访问）
/// POST /a2a/agent/{agentId}  → JSON-RPC 任务接口（公开访问）
///   - tasks/send → 发送消息，SSE 流式返回
///   - tasks/get  → 查询最近任务（不含历史，返回 not-found）
///
/// 仅 ExposeAsA2A=true 的 Agent 可通过此接口访问。
/// </summary>
public static class A2AEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── Agent Card（发现端点）────────────────────────────────────────────
        endpoints.MapGet("/a2a/agent/{agentId}", (string agentId, HttpContext ctx, AgentStore store) =>
        {
            AgentConfig? agent = store.GetById(agentId);
            if (agent is null || !agent.IsEnabled || !agent.ExposeAsA2A)
                return Results.NotFound(new JsonRpcError(-32001, "Agent not found or A2A not enabled."));

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
        })
        .WithTags("A2A");

        // ── JSON-RPC 任务接口 ─────────────────────────────────────────────────
        endpoints.MapPost("/a2a/agent/{agentId}", async (
            string agentId,
            HttpContext ctx,
            AgentStore store,
            AgentRunner runner,
            ProviderConfigStore providerStore,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A");

            AgentConfig? agent = store.GetById(agentId);
            if (agent is null || !agent.IsEnabled || !agent.ExposeAsA2A)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(
                    BuildRpcError(null, -32001, "Agent not found or A2A not enabled.", JsonOpts),
                    JsonOpts,
                    ctx.RequestAborted);
                return;
            }

            // 解析 JSON-RPC 请求
            JsonRpcRequest? rpc;
            try
            {
                rpc = await ctx.Request.ReadFromJsonAsync<JsonRpcRequest>(JsonOpts, ctx.RequestAborted);
            }
            catch (JsonException)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    BuildRpcError(null, -32700, "Parse error: invalid JSON.", JsonOpts),
                    JsonOpts,
                    ctx.RequestAborted);
                return;
            }

            if (rpc is null || rpc.Jsonrpc != "2.0")
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(
                    BuildRpcError(rpc?.Id, -32600, "Invalid JSON-RPC request.", JsonOpts),
                    JsonOpts,
                    ctx.RequestAborted);
                return;
            }

            switch (rpc.Method)
            {
                case "tasks/send":
                    await HandleTaskSendAsync(ctx, agent, rpc, runner, providerStore, logger);
                    break;

                case "tasks/get":
                    // tasks/get 不维护任务状态，统一返回任务未找到
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    await ctx.Response.WriteAsJsonAsync(
                        BuildRpcError(rpc.Id, -32001, "Task not found. This gateway does not persist task state.", JsonOpts),
                        JsonOpts,
                        ctx.RequestAborted);
                    break;

                default:
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(
                        BuildRpcError(rpc.Id, -32601, $"Method '{rpc.Method}' not found.", JsonOpts),
                        JsonOpts,
                        ctx.RequestAborted);
                    break;
            }
        })
        .WithTags("A2A");

        return endpoints;
    }

    // ── tasks/send 处理：SSE 流式输出 ─────────────────────────────────────────

    private static async Task HandleTaskSendAsync(
        HttpContext ctx,
        AgentConfig agent,
        JsonRpcRequest rpc,
        AgentRunner runner,
        ProviderConfigStore providerStore,
        ILogger logger)
    {
        // 解析 tasks/send 参数
        TaskSendParams? taskParams;
        try
        {
            taskParams = rpc.Params?.Deserialize<TaskSendParams>(JsonOpts);
        }
        catch (JsonException ex)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(
                BuildRpcError(rpc.Id, -32602, $"Invalid params: {ex.Message}", JsonOpts),
                JsonOpts,
                ctx.RequestAborted);
            return;
        }

        if (taskParams is null)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(
                BuildRpcError(rpc.Id, -32602, "Missing params for tasks/send.", JsonOpts),
                JsonOpts,
                ctx.RequestAborted);
            return;
        }

        string? textContent = ExtractTextContent(taskParams.Message);
        if (string.IsNullOrWhiteSpace(textContent))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(
                BuildRpcError(rpc.Id, -32602, "Message must contain at least one text part.", JsonOpts),
                JsonOpts,
                ctx.RequestAborted);
            return;
        }

        string taskId = taskParams.Id ?? Guid.NewGuid().ToString("N");
        string providerId = providerStore.GetDefault()?.Id ?? string.Empty;

        var history = new List<SessionMessage>
        {
            new(Id: Guid.NewGuid().ToString("N"), Role: "user", Content: textContent, ThinkContent: null,
                Timestamp: DateTimeOffset.UtcNow, Attachments: null, Source: "a2a")
        };

        CancellationToken ct = ctx.RequestAborted;

        // SSE 响应头
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        // 发送 working 状态
        await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskStatusUpdateEvent(
            Type: "TaskStatusUpdateEvent",
            TaskId: taskId,
            Status: new A2ATaskStatus(State: "working"),
            Final: false), JsonOpts, ct);

        var textBuffer = new System.Text.StringBuilder();

        try
        {
            await foreach (StreamItem item in runner.StreamReActAsync(agent, providerId, history, taskId, ct, source: "a2a"))
            {
                if (item is TokenItem token)
                {
                    textBuffer.Append(token.Content);

                    await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskArtifactUpdateEvent(
                        Type: "TaskArtifactUpdateEvent",
                        TaskId: taskId,
                        Artifact: new TaskArtifact(
                            Name: "response",
                            Parts: [new TextPart(Type: "text", Text: token.Content)]),
                        Final: false), JsonOpts, ct);
                }
            }

            // 结束：completed 状态
            await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskStatusUpdateEvent(
                Type: "TaskStatusUpdateEvent",
                TaskId: taskId,
                Status: new A2ATaskStatus(State: "completed"),
                Final: true), JsonOpts, ct);

            await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
        }
        catch (OperationCanceledException)
        {
            // 客户端断开，静默结束
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A2A tasks/send failed for agent {AgentId}, task {TaskId}", agent.Id, taskId);
            try
            {
                await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskStatusUpdateEvent(
                    Type: "TaskStatusUpdateEvent",
                    TaskId: taskId,
                    Status: new A2ATaskStatus(State: "failed", Message: ex.Message),
                    Final: true), JsonOpts, CancellationToken.None);

                await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                await ctx.Response.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // 响应已关闭，忽略
            }
        }
    }

    // ── 工具方法 ──────────────────────────────────────────────────────────────

    private static string? ExtractTextContent(A2AMessage? message)
    {
        if (message?.Parts is null || message.Parts.Count == 0) return null;

        var texts = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "text" &&
                part.TryGetProperty("text", out var textProp))
            {
                texts.Append(textProp.GetString());
            }
        }

        string result = texts.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static async Task WriteSseRpcAsync<T>(
        HttpResponse response, string? id, T result,
        JsonSerializerOptions opts, CancellationToken ct)
    {
        var envelope = new { jsonrpc = "2.0", id, result };
        string json = JsonSerializer.Serialize(envelope, opts);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static object BuildRpcError(string? id, int code, string message, JsonSerializerOptions opts) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };
}

// ── A2A 数据模型（Agent Card）────────────────────────────────────────────────

public sealed record AgentCard(
    string Name,
    string Description,
    string Url,
    string Version,
    AgentCapabilities Capabilities,
    IReadOnlyList<AgentSkill> Skills);

public sealed record AgentCapabilities(bool Streaming);

public sealed record AgentSkill(string Id, string Name, string Description);

// ── A2A JSON-RPC 请求模型 ─────────────────────────────────────────────────────

public sealed record JsonRpcRequest(
    string Jsonrpc,
    string? Id,
    string Method,
    JsonElement? Params);

public sealed record TaskSendParams(
    string? Id,
    A2AMessage? Message,
    bool? Streaming = true);

public sealed record A2AMessage(
    string Role,
    IReadOnlyList<JsonElement> Parts);

// ── A2A SSE 事件 ─────────────────────────────────────────────────────────────

public sealed record TaskStatusUpdateEvent(
    string Type,
    string TaskId,
    A2ATaskStatus Status,
    bool Final);

public sealed record TaskArtifactUpdateEvent(
    string Type,
    string TaskId,
    TaskArtifact Artifact,
    bool Final);

public sealed record A2ATaskStatus(
    string State,
    string? Message = null);

public sealed record TaskArtifact(
    string Name,
    IReadOnlyList<TextPart> Parts);

public sealed record TextPart(string Type, string Text);

// ── 错误模型（仅用于 Agent Card 端点的 404 响应）──────────────────────────────

public sealed record JsonRpcError(int Code, string Message);
