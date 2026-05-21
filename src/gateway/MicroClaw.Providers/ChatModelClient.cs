using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration;
using MicroClaw.Core.Logging;
using MicroClaw.Providers.Mapping;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;
/// <summary>
/// Chat 类 Provider 的运行时基类。子类只需实现 <see cref="BuildClient"/> 提供底层 <see cref="IChatClient"/>。
/// 基类负责：
/// <list type="bullet">
///   <item>同步对话 <see cref="ChatAsync"/> + 自动 usage 追踪；</item>
///   <item>流式 Agent 循环 <see cref="AgentStreamAsync"/>：内置 <see cref="FunctionInvokingChatClient"/> + <see cref="ChatClientAgent"/>；</item>
///   <item>实例级缓存底层 <see cref="IChatClient"/>，<see cref="OnDisposedAsync"/> 负责释放。</item>
/// </list>
/// </summary>
public abstract class ChatModelClient : ModelProviderObject
{
    private static readonly JsonSerializerOptions s_toolArgJsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    private readonly object _clientLock = new();
    private IChatClient? _client;
    
    protected ChatModelClient(ProviderEntityConfig config, IUsageTracker usageTracker) : base(config, usageTracker)
    {
        ModelKind kind = ProviderConfigOps.ParseModelKind(config.ModelKind);
        if (kind != ModelKind.Chat)
            throw new InvalidOperationException($"ChatModelClient requires ModelKind.Chat (got {kind}).");
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
    
    /// <inheritdoc />
    public override async Task TrackUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens = 0L, long cachedInputTokens = 0L)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (inputTokens <= 0 && outputTokens <= 0) return;
        
        long nonCachedInput = Math.Max(0L, inputTokens - cachedInputTokens);
        ModelPricing pricing = Pricing;
        decimal inputCost = nonCachedInput > 0 && pricing.InputPerMillionTokens.HasValue ? nonCachedInput * pricing.InputPerMillionTokens.Value / 1_000_000m : 0m;
        decimal outputCost = outputTokens > 0 && pricing.OutputPerMillionTokens.HasValue ? outputTokens * pricing.OutputPerMillionTokens.Value / 1_000_000m : 0m;
        decimal cacheInputCost = cachedInputTokens > 0 ? cachedInputTokens * (pricing.CachedInputPerMillionTokens ?? pricing.InputPerMillionTokens ?? 0m) / 1_000_000m : 0m;
        
        try
        {
            await UsageTracker.TrackAsync(ctx.Session.Id, Id, DisplayName, ctx.Source, inputTokens, outputTokens, cachedInputTokens, inputCost, outputCost, cacheInputCost, cacheOutputCostUsd: 0m, agentId: null, monthlyBudgetUsd: null, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Usage tracking failed for provider {ProviderId} session {SessionId}", Id, ctx.Session.Id);
        }
    }
    
    /// <inheritdoc />
    public override async Task<ChatResponse> ChatAsync(MicroChatContext ctx, IEnumerable<ChatMessage> messages, ChatOptions? options = null)
    {
        CancellationToken ct = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(messages);
        
        options ??= BuildDefaultChatOptions();
        
        ChatResponse response = await Client.GetResponseAsync(messages, options, ct);
        
        if (response.Usage is { } usage)
        {
            await TrackUsageAsync(ctx, usage.InputTokenCount ?? 0L, usage.OutputTokenCount ?? 0L, usage.CachedInputTokenCount ?? 0L);
        }
        
        return response;
    }
    
    /// <inheritdoc />
    public override async IAsyncEnumerable<StreamItem> AgentStreamAsync(MicroChatContext ctx)
    {
        CancellationToken ct = ValidateContext(ctx);
        
        IReadOnlyList<ChatMessage> messages = ctx.AssembledMessages ?? throw new InvalidOperationException("AssembledMessages not set on MicroChatContext.");
        IReadOnlyList<AITool> tools = ctx.AssembledTools ?? [];
        IReadOnlySet<string>? internalToolNames = ctx.InternalToolNames;
        
        string agentName = !string.IsNullOrWhiteSpace(ctx.TargetAgentName) ? ctx.TargetAgentName : !string.IsNullOrWhiteSpace(ctx.TargetAgentId) ? ctx.TargetAgentId : "agent";
        
        ChatOptions resolvedOptions = ctx.ExecutionOptions ?? BuildDefaultChatOptions();
        if (resolvedOptions.Tools is null && tools.Count > 0 && Capabilities.HasFlag(ModelCapability.ToolCalling))
            resolvedOptions.Tools = [.. tools];
        
        Channel<StreamItem> output = Channel.CreateUnbounded<StreamItem>(new UnboundedChannelOptions { SingleReader = true });
        
        Task exec = RunStreamingCoreAsync(ctx, resolvedOptions, agentName, internalToolNames, output, ct);
        
        try
        {
            await foreach (StreamItem item in output.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            try
            {
                await exec;
            }
            catch (OperationCanceledException)
            {
                /* 取消时静默 */
            }
            catch
            {
                /* 异常已通过 Channel 传播 */
            }
        }
    }
    
    private async Task RunStreamingCoreAsync(MicroChatContext ctx, ChatOptions options, string agentName, IReadOnlySet<string>? internalToolNames, Channel<StreamItem> output, CancellationToken ct)
    {
        IReadOnlyList<ChatMessage> messages = ctx.AssembledMessages!;
        var tracker = new StreamMessageId();
        var usage = new UsageCaptureBox();
        
        try
        {
            var funcClient = new FunctionInvokingChatClient(Client) { MaximumIterationsPerRequest = ctx.MaxAgentIterations ?? 1, AllowConcurrentInvocation = true, FunctionInvoker = BuildFunctionInvoker(output.Writer, tracker, internalToolNames), };
            
            var agentOptions = new ChatClientAgentOptions { Name = SanitizeAgentName(agentName), UseProvidedChatClientAsIs = true, ChatOptions = options, };
            
            // RunOptions 故意不携带 Tools，避免 MAF 把 agentOptions.ChatOptions.Tools 与 runOptions.Tools
            // 同时下发给模型，导致 Claude 等出现 "function duplicated" 错误。
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
            
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(messages, session: session, runOptions, ct))
            {
                if (!string.IsNullOrEmpty(update.MessageId))
                    tracker.Current = update.MessageId;
                
                foreach (StreamItem item in ConvertContents(update.Contents, tracker.Current, usage))
                    await output.Writer.WriteAsync(item, ct);
            }
            
            if (usage.Last is { } usageDetails)
            {
                await TrackUsageAsync(ctx, usageDetails.InputTokenCount ?? 0L, usageDetails.OutputTokenCount ?? 0L, usageDetails.CachedInputTokenCount ?? 0L);
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
    
    private static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> BuildFunctionInvoker(ChannelWriter<StreamItem> writer, StreamMessageId tracker, IReadOnlySet<string>? internalToolNames)
    {
        return async (FunctionInvocationContext fctx, CancellationToken ct) =>
        {
            string callId = fctx.CallContent?.CallId ?? fctx.Function.Name;
            IDictionary<string, object?>? args = fctx.Arguments?.ToDictionary(k => k.Key, v => v.Value);
            string? messageId = tracker.Current;
            string? visibility = internalToolNames is not null && internalToolNames.Contains(fctx.Function.Name) ? MessageVisibility.LlmOnly : null;
            
            await writer.WriteAsync(new ToolCallItem(callId, fctx.Function.Name, args) { MessageId = messageId, Visibility = visibility, }, ct);
            
            // AsyncLocal 桥接：让子代理运行器可以向父事件流写入进度事件
            SubAgentEventBridge.Current = writer;
            
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
            
            await writer.WriteAsync(new ToolResultItem(callId, fctx.Function.Name, resultText, success, sw.ElapsedMilliseconds) { MessageId = messageId, Visibility = visibility, }, ct);
            
            return success ? result : resultText;
        };
    }
    
    private static IEnumerable<StreamItem> ConvertContents(IList<AIContent> contents, string? messageId, UsageCaptureBox usage)
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
                    yield return new DataContentItem(dc.MediaType ?? "application/octet-stream", dc.Data.ToArray()) { MessageId = messageId };
                    break;
                
                case UsageContent uc:
                    usage.Last = uc.Details;
                    break;
            }
        }
    }
    
    /// <summary>构造默认 <see cref="ChatOptions"/>。</summary>
    protected virtual ChatOptions BuildDefaultChatOptions() =>
        new()
        {
            ModelId = ModelName, MaxOutputTokens = MaxOutputTokens, ToolMode = ChatToolMode.Auto, AllowMultipleToolCalls = true,
        };
    
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
    protected override async ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
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
        await base.OnDisposedAsync(cancellationToken);
    }
    public override void RefreshClient()
    {
        IChatClient? old;
        lock (_clientLock)
        {
            old = _client;
            _client = null;
        }
        (old as IDisposable)?.Dispose();
    }
    
    private sealed class StreamMessageId
    {
        public string? Current { get; set; }
    }
    
    private sealed class UsageCaptureBox
    {
        public UsageDetails? Last { get; set; }
    }
}