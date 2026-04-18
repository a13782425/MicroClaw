using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Core.Logging;
using MicroClaw.Infrastructure.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// Chat 类 Provider 的抽象基类。具体子类只需实现 <see cref="BuildClient"/>，
/// 提供对应 SDK 封装的 <see cref="IChatClient"/>；基类负责：
/// <list type="bullet">
///   <item>同步单轮对话 <see cref="ChatAsync"/> + 自动 usage 追踪；</item>
///   <item>
///     流式 Agent 循环 <see cref="StreamAgentAsync"/>：内部组合
///     <see cref="FunctionInvokingChatClient"/> + <see cref="ChatClientAgent"/>，
///     在每次工具调用前后写入 <see cref="ToolCallItem"/> / <see cref="ToolResultItem"/>，
///     并在结束时汇总 <see cref="UsageDetails"/> 上报追踪器；
///   </item>
///   <item>实例级复用底层 <see cref="IChatClient"/>；<see cref="OnDisposeAsync"/> 负责释放。</item>
/// </list>
/// </summary>
public abstract class ChatMicroProvider : MicroProvider
{
    private static readonly JsonSerializerOptions s_toolArgJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _clientLock = new();
    private IChatClient? _client;

    /// <summary>创建 Chat 类 Provider。</summary>
    protected ChatMicroProvider(ProviderConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker)
    {
    }

    /// <summary>懒加载的底层 <see cref="IChatClient"/>。同一实例内复用。</summary>
    protected IChatClient Client
    {
        get
        {
            if (_client is not null) return _client;
            lock (_clientLock)
            {
                _client ??= BuildClient();
                return _client;
            }
        }
    }

    /// <summary>构造底层 <see cref="IChatClient"/> 的工厂方法，由具体 Provider 子类实现。</summary>
    protected abstract IChatClient BuildClient();

    /// <summary>
    /// 使用默认 <see cref="ChatOptions"/>（ModelId/MaxOutputTokens 取自 <see cref="MicroProvider.Config"/>）
    /// 发送一次非流式对话，并在响应可观测到 <see cref="UsageDetails"/> 时自动调用
    /// <see cref="MicroProvider.TrackChatUsageAsync"/>。
    /// </summary>
    /// <param name="ctx">统一调用上下文（提供 Session/Source/Ct）。</param>
    /// <param name="messages">完整消息序列（包含 system / user / assistant / tool 等）。</param>
    /// <param name="options">
    ///     可选的 <see cref="ChatOptions"/> 覆盖。传入 <c>null</c> 时使用
    ///     <see cref="BuildDefaultChatOptions"/> 的默认值；传入非 null 时按原值下发，
    ///     基类不再强制覆盖 ModelId / MaxOutputTokens。
    /// </param>
    public virtual async Task<ChatResponse> ChatAsync(
        MicroChatContext ctx,
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null)
    {
        CancellationToken ct = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(messages);

        options ??= BuildDefaultChatOptions();

        ChatResponse response = await Client.GetResponseAsync(messages, options, ct);

        if (response.Usage is { } usage)
        {
            await TrackChatUsageAsync(
                ctx,
                usage.InputTokenCount ?? 0L,
                usage.OutputTokenCount ?? 0L,
                usage.CachedInputTokenCount ?? 0L);
        }

        return response;
    }

    /// <summary>
    /// 使用 <see cref="FunctionInvokingChatClient"/> + <see cref="ChatClientAgent"/> 驱动一次
    /// 完整的 Agent 循环：模型产出 token 和 thinking → 命中工具调用 → 执行工具 → 写入结果 →
    /// 继续循环直到模型给出最终回复。
    /// <para>
    /// 产出的 <see cref="StreamItem"/> 序列包含：<see cref="TokenItem"/>、<see cref="ThinkingItem"/>、
    /// <see cref="DataContentItem"/>（图片/音频等）、<see cref="ToolCallItem"/>、<see cref="ToolResultItem"/>。
    /// </para>
    /// </summary>
    /// <param name="ctx">统一调用上下文；<see cref="MicroChatContext.Ct"/> 与 <paramref name="ct"/> 之间以参数为准（通常两者相同）。</param>
    /// <param name="messages">初始消息序列。</param>
    /// <param name="tools">工具列表；为空表示禁止 function calling。</param>
    /// <param name="options">
    ///     可选的 <see cref="ChatOptions"/> 覆盖；传入 <c>null</c> 时由
    ///     <see cref="BuildDefaultChatOptions"/> 构造并自动挂载 <paramref name="tools"/>。
    /// </param>
    /// <param name="internalToolNames">
    ///     可选的"内部工具"名单。命中时对应的 <see cref="ToolCallItem"/> / <see cref="ToolResultItem"/>
    ///     的 <see cref="StreamItem.Visibility"/> 会被设为
    ///     <see cref="MessageVisibility.LlmOnly"/>，避免前端展示。
    /// </param>
    /// <param name="ct">取消令牌；与 <paramref name="ctx"/>.Ct 通常一致。</param>
    public virtual async IAsyncEnumerable<StreamItem> StreamAgentAsync(
        MicroChatContext ctx,
        IEnumerable<ChatMessage> messages,
        IReadOnlyList<AITool> tools,
        ChatOptions? options = null,
        IReadOnlySet<string>? internalToolNames = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _ = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        // TODO: 待 MicroChatContext 扩展 Agent 元数据后，AgentName / MaxIterations 等应由上下文提供。
        const string agentName = "agent";
        const int maxIterations = 10;

        ChatOptions resolvedOptions = options ?? BuildDefaultChatOptions();
        if (resolvedOptions.Tools is null && tools.Count > 0 && Config.Capabilities.SupportsFunctionCalling)
            resolvedOptions.Tools = [.. tools];

        Channel<StreamItem> output = Channel.CreateUnbounded<StreamItem>(
            new UnboundedChannelOptions { SingleReader = true });

        Task exec = RunStreamingCoreAsync(
            ctx, messages, resolvedOptions, agentName, maxIterations, internalToolNames, output, ct);

        try
        {
            await foreach (StreamItem item in output.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            try { await exec; }
            catch (OperationCanceledException) { /* 取消时静默 */ }
            catch { /* 异常已通过 Channel 传播 */ }
        }
    }

    // ── 内部：非迭代器流式核心（允许使用 try-catch 和 finally）──────────────
    private async Task RunStreamingCoreAsync(
        MicroChatContext ctx,
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        string agentName,
        int maxIterations,
        IReadOnlySet<string>? internalToolNames,
        Channel<StreamItem> output,
        CancellationToken ct)
    {
        var tracker = new StreamMessageIdTracker();
        var usage = new UsageCaptureBox();

        try
        {
            var funcClient = new FunctionInvokingChatClient(Client)
            {
                MaximumIterationsPerRequest = maxIterations,
                AllowConcurrentInvocation = true,
                FunctionInvoker = BuildFunctionInvoker(output.Writer, tracker, internalToolNames),
            };

            var agentOptions = new ChatClientAgentOptions
            {
                Name = SanitizeAgentName(agentName),
                UseProvidedChatClientAsIs = true,
                ChatOptions = options,
            };

            // RunOptions 故意不携带 Tools，避免 MAF 把 agentOptions.ChatOptions.Tools 和 runOptions.Tools
            // 同时下发给模型，导致部分 Provider（如 Claude）出现 "function duplicated" 错误。
            var runOptions = new ChatClientAgentRunOptions(new ChatOptions
            {
                ModelId = options.ModelId,
                MaxOutputTokens = options.MaxOutputTokens,
                ToolMode = options.ToolMode,
                AllowMultipleToolCalls = options.AllowMultipleToolCalls,
                AdditionalProperties = options.AdditionalProperties,
            });

            ChatClientAgent agent = new(funcClient, agentOptions, loggerFactory: null, services: null);
            AgentSession session = await agent.CreateSessionAsync(ct);

            await foreach (AgentResponseUpdate update in
                           agent.RunStreamingAsync(messages, session: session, runOptions, ct))
            {
                if (!string.IsNullOrEmpty(update.MessageId))
                    tracker.Current = update.MessageId;

                foreach (StreamItem item in ConvertContents(update.Contents, tracker.Current, usage))
                    await output.Writer.WriteAsync(item, ct);
            }

            if (usage.Last is { } usageDetails)
            {
                await TrackChatUsageAsync(
                    ctx,
                    usageDetails.InputTokenCount ?? 0L,
                    usageDetails.OutputTokenCount ?? 0L,
                    usageDetails.CachedInputTokenCount ?? 0L);
            }

            output.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            output.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            output.Writer.TryComplete(ex);
        }
    }

    // ── 工具调用事件桥接 ────────────────────────────────────────────────
    private Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> BuildFunctionInvoker(
        ChannelWriter<StreamItem> writer,
        StreamMessageIdTracker tracker,
        IReadOnlySet<string>? internalToolNames)
    {
        return async (FunctionInvocationContext fctx, CancellationToken ct) =>
        {
            string callId = fctx.CallContent?.CallId ?? fctx.Function.Name;
            IDictionary<string, object?>? args = fctx.Arguments?.ToDictionary(k => k.Key, v => v.Value);
            string? messageId = tracker.Current;
            string? visibility = internalToolNames is not null && internalToolNames.Contains(fctx.Function.Name)
                ? MessageVisibility.LlmOnly
                : null;

            // ① 工具调用事件（执行前）
            await writer.WriteAsync(
                new ToolCallItem(callId, fctx.Function.Name, args)
                {
                    MessageId = messageId,
                    Visibility = visibility,
                }, ct);

            // ② AsyncLocal 桥接：让子代理运行器可以向父事件流写入进度事件
            SubAgentEventBridge.Current = writer;

            // TODO: 待 MicroChatContext 的 MicroChatLifecyclePhase 钩子接线后，
            //       在此触发 PreToolUse / PostToolUse / ToolUseFailure。
            //       本轮重构暂时移除 IHookExecutor 调用，等 Pet 管线重新驱动。

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool success = true;
            object? result = null;
            try
            {
                result = await fctx.Function.InvokeAsync(fctx.Arguments, ct);
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
                _ => JsonSerializer.Serialize(result, s_toolArgJsonOpts),
            };

            // TODO: 待 IDevMetricsService 通过 MicroChatContext 或专用通道再次接线时恢复调用。

            await writer.WriteAsync(
                new ToolResultItem(callId, fctx.Function.Name, resultText, success, sw.ElapsedMilliseconds)
                {
                    MessageId = messageId,
                    Visibility = visibility,
                }, ct);

            return success ? result : resultText;
        };
    }

    // ── AIContent → StreamItem 转换（内联 TextContent/ThinkingContent/DataContent/UsageContent）──
    private static IEnumerable<StreamItem> ConvertContents(
        IList<AIContent> contents,
        string? messageId,
        UsageCaptureBox usage)
    {
        foreach (AIContent content in contents)
        {
            switch (content)
            {
                case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                    yield return new TokenItem(tc.Text) { MessageId = messageId };
                    break;

                case TextReasoningContent rc when !string.IsNullOrEmpty(rc.Text):
                    yield return new ThinkingItem(rc.Text) { MessageId = messageId };
                    break;

                case DataContent dc when dc.Data is { Length: > 0 }:
                    yield return new DataContentItem(
                        dc.MediaType ?? "application/octet-stream", dc.Data.ToArray())
                    { MessageId = messageId };
                    break;

                case UsageContent uc:
                    usage.Last = uc.Details;
                    break;
            }
        }
    }

    /// <summary>根据 <see cref="MicroProvider.Config"/> 构造默认 <see cref="ChatOptions"/>。</summary>
    protected virtual ChatOptions BuildDefaultChatOptions() => new()
    {
        ModelId = Config.ModelName,
        MaxOutputTokens = Config.MaxOutputTokens,
        ToolMode = ChatToolMode.Auto,
        AllowMultipleToolCalls = true,
    };

    /// <summary>将 Agent 名称清洗成合法的函数/工具名（字母数字下划线，不超过 64 字符）。</summary>
    private static string SanitizeAgentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "agent";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string trimmed = sb.ToString().Trim('_');
        if (trimmed.Length == 0) return "agent";
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        IChatClient? client = Interlocked.Exchange(ref _client, null);
        switch (client)
        {
            case IAsyncDisposable ad:
                await ad.DisposeAsync();
                break;
            case IDisposable d:
                d.Dispose();
                break;
        }
    }

    // ── 私有辅助类型 ────────────────────────────────────────────────────
    private sealed class StreamMessageIdTracker
    {
        public string? Current { get; set; }
    }

    private sealed class UsageCaptureBox
    {
        public UsageDetails? Last { get; set; }
    }
}
