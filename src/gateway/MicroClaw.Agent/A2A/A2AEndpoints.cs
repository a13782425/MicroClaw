using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.A2A;

/// <summary>
/// A2A��Agent-to-Agent��Э��˵㡣
/// ʵ�� A2A v0.2 �淶��https://google.github.io/A2A���� JSON-RPC �ӿڡ�
/// 
/// GET  /a2a/agent/{agentId}  �� Agent Card�����ֶ˵㣬�������ʣ�
/// POST /a2a/agent/{agentId}  �� JSON-RPC ����ӿڣ��������ʣ�
///   - tasks/send �� ������Ϣ��SSE ��ʽ����
///   - tasks/get  �� ��ѯ������񣨲�����ʷ������ not-found��
///
/// �� ExposeAsA2A=true �� Agent ��ͨ���˽ӿڷ��ʡ�
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
        // ���� Agent Card�����ֶ˵㣩����������������������������������������������������������������������������������������
        endpoints.MapGet("/a2a/agent/{agentId}", (string agentId, HttpContext ctx, IAgentRepository agentRepo) =>
        {
            AgentDto? agent = agentRepo.GetById(agentId);
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

        // ���� JSON-RPC ����ӿ� ��������������������������������������������������������������������������������������������������
        endpoints.MapPost("/a2a/agent/{agentId}", async (
            string agentId,
            HttpContext ctx,
            IAgentRepository agentRepo,
            IMicroAgentService agentService,
            ProviderService providerStore,
            IServiceProvider sp,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A");

            AgentDto? agent = agentRepo.GetById(agentId);
            if (agent is null || !agent.IsEnabled || !agent.ExposeAsA2A)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsJsonAsync(
                    BuildRpcError(null, -32001, "Agent not found or A2A not enabled.", JsonOpts),
                    JsonOpts,
                    ctx.RequestAborted);
                return;
            }

            // ���� JSON-RPC ����
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
                    await HandleTaskSendAsync(ctx, agent, rpc, agentService, providerStore, sp, logger);
                    break;

                case "tasks/get":
                    // tasks/get ��ά������״̬��ͳһ��������δ�ҵ�
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

    // ���� tasks/send �����SSE ��ʽ��� ����������������������������������������������������������������������������������

    private static async Task HandleTaskSendAsync(
        HttpContext ctx,
        AgentDto agent,
        JsonRpcRequest rpc,
        IMicroAgentService agentService,
        ProviderService providerStore,
        IServiceProvider sp,
        ILogger logger)
    {
        // ���� tasks/send ����
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
        ProviderEntity? providerCfg = providerStore.GetDefault();
        string providerId = providerCfg?.Id ?? string.Empty;
        if (providerCfg is null)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsJsonAsync(
                BuildRpcError(rpc.Id, -32099, "No provider available.", JsonOpts),
                JsonOpts,
                ctx.RequestAborted);
            return;
        }

        IMicroAgent runtimeAgent = agentService.GetById(agent.Id)
            ?? throw new InvalidOperationException($"Agent '{agent.Id}' not found in runtime cache.");

        var history = new List<SessionMessage>
        {
            new(Id: Guid.NewGuid().ToString("N"), Role: "user", Content: textContent, ThinkContent: null,
                Timestamp: DateTimeOffset.UtcNow, Attachments: null, Source: "a2a")
        };

        CancellationToken ct = ctx.RequestAborted;

        // SSE ��Ӧͷ
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        // ���� working ״̬
        await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskStatusUpdateEvent(
            Type: "TaskStatusUpdateEvent",
            TaskId: taskId,
            Status: new A2ATaskStatus(State: "working"),
            Final: false), JsonOpts, ct);

        var textBuffer = new System.Text.StringBuilder();

        // Assemble messages
        var messageAssembler = ActivatorUtilities.CreateInstance<ChatMessageAssembler>(sp);
        ChatMessageAssemblyResult assembly = await messageAssembler.AssembleAsync(
            agent, providerCfg, history, taskId, ct: ct);

        // Collect tools and build MicroChatContext
        var toolCollector = sp.GetRequiredService<ToolCollector>();
        var toolCtx = new ToolCreationContext(SessionId: taskId, CallingAgentId: agent.Id);
        ToolCollectionResult? toolResult = null;
        try
        {
            toolResult = await toolCollector.CollectToolsAsync(agent, toolCtx, ct);

            var availableToolNames = toolResult.AllTools
                .Select(static t => t.Name)
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlySet<string> internalToolNames = SkillToolProvider.InternalToolNames
                .Where(availableToolNames.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ChatOptions executionOptions = ChatExecutionOptionsFactory.Build(toolResult.AllTools, providerCfg);
            IMicroSession a2aSession = MicroChatContext.ForSystem(taskId, "a2a", ct).Session;
            var chatCtx = new MicroChatContext
            {
                Session = a2aSession,
                Source = "a2a",
                History = history,
                Ct = ct,
                TargetProviderId = providerCfg.Id,
                AssembledMessages = assembly.Messages,
                AssembledTools = toolResult.AllTools,
                InternalToolNames = internalToolNames,
                ExecutionOptions = executionOptions,
            };

            try
            {
                await foreach (StreamItem item in runtimeAgent.StreamAsync(chatCtx))
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

                // ������completed ״̬
                await WriteSseRpcAsync(ctx.Response, rpc.Id, new TaskStatusUpdateEvent(
                    Type: "TaskStatusUpdateEvent",
                    TaskId: taskId,
                    Status: new A2ATaskStatus(State: "completed"),
                    Final: true), JsonOpts, ct);

                await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
            }
            catch (OperationCanceledException)
            {
                // �ͻ��˶Ͽ�����Ĭ����
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
                    // ��Ӧ�ѹرգ�����
                }
            }
        }
        finally
        {
            if (toolResult is not null)
                await toolResult.DisposeAsync();
        }
    }

    // ���� ���߷��� ����������������������������������������������������������������������������������������������������������������������������

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

// ���� A2A ����ģ�ͣ�Agent Card��������������������������������������������������������������������������������������������������

public sealed record AgentCard(
    string Name,
    string Description,
    string Url,
    string Version,
    AgentCapabilities Capabilities,
    IReadOnlyList<AgentSkill> Skills);

public sealed record AgentCapabilities(bool Streaming);

public sealed record AgentSkill(string Id, string Name, string Description);

// ���� A2A JSON-RPC ����ģ�� ����������������������������������������������������������������������������������������������������������

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

// ���� A2A SSE �¼� ��������������������������������������������������������������������������������������������������������������������������

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

// ���� ����ģ�ͣ������� Agent Card �˵�� 404 ��Ӧ��������������������������������������������������������������

public sealed record JsonRpcError(int Code, string Message);

