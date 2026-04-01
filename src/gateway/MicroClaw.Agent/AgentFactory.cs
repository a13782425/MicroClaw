using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.Dev;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Plugins.Hooks;
using MicroClaw.Safety;
using MicroClaw.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// 动态构建 <see cref="ChatClientAgent"/> 实例，并将工具调用事件桥接到 <see cref="Channel{T}"/>。
/// <para>
/// 内部使用 MEAI 的 <see cref="FunctionInvokingChatClient"/>（<c>UseProvidedChatClientAsIs=true</c>），
/// 通过 <c>FunctionInvoker</c> 委托在每次工具调用前后写入 <see cref="ToolCallItem"/> / <see cref="ToolResultItem"/>。
/// 调用方负责同时消费 <c>agent.RunStreamingAsync()</c> 的 token 流和事件 Channel 的事件流并合并输出。
/// </para>
/// </summary>
internal static class AgentFactory
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// 创建 <see cref="ChatClientAgent"/> 及共享的事件 <see cref="Channel{StreamItem}"/>。
    /// 工具调用事件（<see cref="ToolCallItem"/> / <see cref="ToolResultItem"/>）会被写入该 Channel，
    /// 需由调用方与 token 流合并后一起推送给 SSE。
    /// </summary>
    /// <param name="baseClient">原始 <see cref="IChatClient"/>（无工具调用封装）。</param>
    /// <param name="agentName">Agent 名称（会被清理为合法函数名）。</param>
    /// <param name="chatOptions">包含 Tools/ModelId/MaxOutputTokens 等配置的 ChatOptions。</param>
    /// <param name="loggerFactory"><see cref="ILoggerFactory"/>。</param>
    /// <param name="maxIterations">最大工具调用轮次（默认 10）。</param>
    /// <param name="devMetrics">可选的开发指标服务，非 null 时在每次工具调用后记录耗时。</param>
    /// <param name="riskRegistry">可选的工具风险注册表，用于查询工具调用前的风险等级。</param>
    /// <param name="riskInterceptor">可选的风险拦截器，在工具执行前执行前置风险检查。</param>
    /// <returns>(<see cref="ChatClientAgent"/>, <see cref="Channel{StreamItem}"/> eventChannel, <see cref="ChatClientAgentRunOptions"/> runOptions, <see cref="MessageIdTracker"/> tracker)</returns>
    public static (ChatClientAgent Agent, Channel<StreamItem> EventChannel, ChatClientAgentRunOptions RunOptions, MessageIdTracker Tracker) Create(
        IChatClient baseClient,
        string agentName,
        ChatOptions chatOptions,
        ILoggerFactory loggerFactory,
        int maxIterations = 10,
        IDevMetricsService? devMetrics = null,
        IToolRiskRegistry? riskRegistry = null,
        IToolRiskInterceptor? riskInterceptor = null,
        IHookExecutor? hookExecutor = null)
    {
        Channel<StreamItem> eventChannel = Channel.CreateUnbounded<StreamItem>(
            new UnboundedChannelOptions { SingleReader = true });

        var tracker = new MessageIdTracker();

        // 绑定 FunctionInvoker：在工具执行前后写入事件
        var funcClient = new FunctionInvokingChatClient(baseClient, loggerFactory)
        {
            MaximumIterationsPerRequest = maxIterations,
            AllowConcurrentInvocation = true,
            FunctionInvoker = BuildFunctionInvoker(eventChannel.Writer, tracker, devMetrics, riskRegistry, riskInterceptor, hookExecutor)
        };

        // UseProvidedChatClientAsIs = true：让我们自己的 FunctionInvokingChatClient 负责工具循环
        var agentOptions = new ChatClientAgentOptions
        {
            Name = Sanitize(agentName),
            UseProvidedChatClientAsIs = true,
            ChatOptions = chatOptions
        };

        // RunOptions 故意不带 Tools，避免 MAF 将 agentOptions.ChatOptions.Tools 与 runOptions.Tools
        // 合并后发送给 LLM，导致工具名称重复（Claude API 会报 "function duplicated" 错误）。
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            ModelId = chatOptions.ModelId,
            MaxOutputTokens = chatOptions.MaxOutputTokens,
            ToolMode = chatOptions.ToolMode,
            AllowMultipleToolCalls = chatOptions.AllowMultipleToolCalls,
            AdditionalProperties = chatOptions.AdditionalProperties,
        });

        ChatClientAgent agent = new(funcClient, agentOptions, loggerFactory, services: null);
        return (agent, eventChannel, runOptions, tracker);
    }

    // ── 私有辅助 ───────────────────────────────────────────────────────────

    /// <summary>构建 FunctionInvoker 委托：拦截工具调用并向事件 Channel 写入事件，可选上报开发指标，可选风险前置拦截。</summary>
    private static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> BuildFunctionInvoker(
        ChannelWriter<StreamItem> eventWriter,
        MessageIdTracker tracker,
        IDevMetricsService? devMetrics,
        IToolRiskRegistry? riskRegistry,
        IToolRiskInterceptor? riskInterceptor,
        IHookExecutor? hookExecutor)
    {
        return async (FunctionInvocationContext ctx, CancellationToken ct) =>
        {
            string callId = ctx.CallContent?.CallId ?? ctx.Function.Name;
            IDictionary<string, object?>? args = ctx.Arguments?.ToDictionary(k => k.Key, v => v.Value);
            string? messageId = tracker.Current;
            string? visibility = SkillToolProvider.InternalToolNames.Contains(ctx.Function.Name)
                ? MessageVisibility.LlmOnly
                : null;

            // ① 发出工具调用事件（执行前）
            await eventWriter.WriteAsync(new ToolCallItem(callId, ctx.Function.Name, args) { MessageId = messageId, Visibility = visibility }, ct);

            // ② 设置 AsyncLocal 桥接，让子代理运行器可以向父 SSE 流写入进度事件
            SubAgentEventBridge.Current = eventWriter;

            // ②b 插件 Hook：PreToolUse
            if (hookExecutor is not null)
            {
                var hookCtx = new HookContext
                {
                    Event = HookEvent.PreToolUse,
                    ToolName = ctx.Function.Name,
                    ToolArguments = args
                };
                HookResult hookResult = await hookExecutor.ExecuteAsync(hookCtx, ct);
                if (hookResult.Decision == HookDecision.Deny)
                {
                    string blockMsg = hookResult.DenyReason ?? "工具调用被插件 Hook 拒绝";
                    await eventWriter.WriteAsync(
                        new ToolResultItem(callId, ctx.Function.Name, $"[BLOCKED] {blockMsg}", false, 0) { MessageId = messageId, Visibility = visibility }, ct);
                    return $"[BLOCKED] {blockMsg}";
                }
            }

            // ③ 风险前置拦截（仅当注册表和拦截器均配置时生效）
            if (riskRegistry is not null && riskInterceptor is not null)
            {
                RiskLevel riskLevel = riskRegistry.GetRiskLevel(ctx.Function.Name);
                ToolInterceptResult interceptResult = await riskInterceptor.InterceptAsync(ctx.Function.Name, riskLevel, args, ct);
                if (!interceptResult.IsAllowed)
                {
                    string blockMsg = interceptResult.BlockReason ?? "工具调用被风险拦截器阻止";
                    await eventWriter.WriteAsync(
                        new ToolResultItem(callId, ctx.Function.Name, $"[BLOCKED] {blockMsg}", false, 0) { MessageId = messageId, Visibility = visibility }, ct);
                    return $"[BLOCKED] {blockMsg}";
                }
            }

            var sw = Stopwatch.StartNew();
            bool success = true;
            object? result = null;
            try
            {
                result = await ctx.Function.InvokeAsync(ctx.Arguments, ct);
            }
            catch (Exception ex)
            {
                success = false;
                result = $"Error: {ex.Message}";

                // 插件 Hook：PostToolUseFailure
                if (hookExecutor is not null)
                {
                    var failCtx = new HookContext
                    {
                        Event = HookEvent.PostToolUseFailure,
                        ToolName = ctx.Function.Name,
                        ToolArguments = args,
                        ToolSuccess = false,
                        ErrorMessage = ex.Message
                    };
                    _ = hookExecutor.ExecuteAsync(failCtx, CancellationToken.None);
                }
            }
            finally
            {
                sw.Stop();
            }

            string resultText = result switch
            {
                string s => s,
                null => string.Empty,
                _ => JsonSerializer.Serialize(result, JsonOpts)
            };

            // ④ 上报工具执行指标（可选，Development 调试用）
            devMetrics?.RecordToolExecution(ctx.Function.Name, sw.ElapsedMilliseconds, success);

            // ⑤ 发出工具结果事件（执行后）
            await eventWriter.WriteAsync(
                new ToolResultItem(callId, ctx.Function.Name, resultText, success, sw.ElapsedMilliseconds) { MessageId = messageId, Visibility = visibility }, ct);
            // ⑦ 插件 Hook：PostToolUse
            if (hookExecutor is not null && success)
            {
                var postCtx = new HookContext
                {
                    Event = HookEvent.PostToolUse,
                    ToolName = ctx.Function.Name,
                    ToolArguments = args,
                    ToolResult = resultText,
                    ToolSuccess = true
                };
                _ = hookExecutor.ExecuteAsync(postCtx, CancellationToken.None);
            }
            return success ? result : resultText;
        };
    }

    /// <summary>将 Agent 名称清理为合法的函数/工具名（字母数字下划线，不超过 64 字符）。</summary>
    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "agent";
        var sanitized = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sanitized.Append(char.IsLetterOrDigit(c) ? c : '_');
        string result = sanitized.ToString().Trim('_');
        return result.Length == 0 ? "agent" : result.Length > 64 ? result[..64] : result;
    }
}
