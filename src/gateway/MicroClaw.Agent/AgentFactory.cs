using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.Dev;
using MicroClaw.Gateway.Contracts.Streaming;
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
    /// <returns>(<see cref="ChatClientAgent"/>, <see cref="Channel{StreamItem}"/> eventChannel, <see cref="ChatClientAgentRunOptions"/> runOptions)</returns>
    public static (ChatClientAgent Agent, Channel<StreamItem> EventChannel, ChatClientAgentRunOptions RunOptions) Create(
        IChatClient baseClient,
        string agentName,
        ChatOptions chatOptions,
        ILoggerFactory loggerFactory,
        int maxIterations = 10,
        IDevMetricsService? devMetrics = null)
    {
        Channel<StreamItem> eventChannel = Channel.CreateUnbounded<StreamItem>(
            new UnboundedChannelOptions { SingleReader = true });

        // 绑定 FunctionInvoker：在工具执行前后写入事件
        var funcClient = new FunctionInvokingChatClient(baseClient, loggerFactory)
        {
            MaximumIterationsPerRequest = maxIterations,
            AllowConcurrentInvocation = true,
            FunctionInvoker = BuildFunctionInvoker(eventChannel.Writer, devMetrics)
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
        return (agent, eventChannel, runOptions);
    }

    // ── 私有辅助 ───────────────────────────────────────────────────────────

    /// <summary>构建 FunctionInvoker 委托：拦截工具调用并向事件 Channel 写入事件，可选上报开发指标。</summary>
    private static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> BuildFunctionInvoker(
        ChannelWriter<StreamItem> eventWriter,
        IDevMetricsService? devMetrics)
    {
        return async (FunctionInvocationContext ctx, CancellationToken ct) =>
        {
            string callId = ctx.CallContent?.CallId ?? ctx.Function.Name;
            IDictionary<string, object?>? args = ctx.Arguments?.ToDictionary(k => k.Key, v => v.Value);

            // ① 发出工具调用事件（执行前）
            await eventWriter.WriteAsync(new ToolCallItem(callId, ctx.Function.Name, args), ct);

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

            // ② 上报工具执行指标（可选，Development 调试用）
            devMetrics?.RecordToolExecution(ctx.Function.Name, sw.ElapsedMilliseconds, success);

            // ③ 发出工具结果事件（执行后）
            await eventWriter.WriteAsync(
                new ToolResultItem(callId, ctx.Function.Name, resultText, success, sw.ElapsedMilliseconds), ct);

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
