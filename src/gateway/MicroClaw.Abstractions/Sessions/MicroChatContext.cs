using System.Threading.Channels;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;

namespace MicroClaw.Abstractions;

/// <summary>
/// 模型调用的统一上下文：既用于 Pet 对话管线，也用于系统级（定时任务、后台审计）调用。
/// <para>
/// 由 <c>MicroPet</c> 在消息处理入口构造 Pet 路径所需的完整上下文；系统路径可通过
/// <see cref="ForSystem(IMicroSession, string, CancellationToken)"/> 快速构造最小上下文。
/// 组件之间可以通过 <see cref="Items"/> 字典进行松耦合的数据交换；对特定阶段有明确语义的字段
/// （<see cref="FinalAssistantMessage"/> / <see cref="CurrentToolCall"/> / <see cref="LastToolResult"/>）
/// 在对应阶段之外可能为 <c>null</c>。
/// </para>
/// <para>
/// 本类型不是线程安全的——单次请求内通常由编排器串行驱动各组件，组件需要后台并行时自行同步。
/// </para>
/// </summary>
public sealed class MicroChatContext
{
    /// <summary>
    /// <see cref="Items"/> 中存放运行时工具分组覆盖配置的约定键。
    /// 值类型约定为 <c>IReadOnlyList&lt;ToolGroupConfig&gt;</c>，但该类型位于更高层模块，
    /// 因此抽象层仅定义键名，不直接引用具体类型。
    /// </summary>
    public const string RuntimeToolGroupOverridesItemKey = "pet:tool-group-overrides";

    /// <summary>所属会话（Pet 宿主）。Provider 用来标记 usage 归属。</summary>
    public required IMicroSession Session { get; init; }

    /// <summary>
    /// 消息来源标签，常见值：<c>"chat"</c>（前端 API）、<c>"channel"</c>（渠道 webhook）、
    /// <c>"heartbeat"</c>（Pet 自主心跳触发）、<c>"rag-audit"</c>（RAG 审计）、<c>"dreaming"</c>/<c>"memory-summary"</c> 等。
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 本次对话前已经加载的消息历史（含刚刚保存的用户消息，若调用方负责保存）。
    /// 系统级调用（<see cref="ForSystem"/>）可能为 <c>null</c>。
    /// </summary>
    public IReadOnlyList<SessionMessage>? History { get; init; }

    /// <summary>
    /// 当前对话的宠物。系统级调用（非 Pet 编排）可能为 <c>null</c>。
    /// </summary>
    public IPet? Pet { get; init; }

    /// <summary>
    /// 当前对话的渠道。系统级调用（非渠道触发）可能为 <c>null</c>。
    /// </summary>
    public IChannel? Channel { get; init; }

    /// <summary>
    /// 流式事件写入口；<c>MicroProvider</c> 在 <c>StreamAgentAsync</c> 时将工具调用/结果事件写入此处。
    /// Pet 启用分支由 <c>MicroPet</c> 在请求结束后负责 Complete；系统调用通常不提供此字段。
    /// </summary>
    public ChannelWriter<StreamItem>? Output { get; init; }

    /// <summary>贯穿整条链的取消令牌。</summary>
    public CancellationToken Ct { get; init; }

    /// <summary>组件间松耦合共享数据的扩展字典，键建议使用"<c>组件名:字段名</c>"命名空间。</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Pet 为本次 dispatch 选定的目标 Agent ID。<c>null</c> 表示尚未完成编排或该系统路径不经 Agent 执行。
    /// </summary>
    public string? TargetAgentId { get; set; }

    /// <summary>
    /// Pet 为本次 dispatch 选定的目标 Agent 名称，供执行层创建模型侧 agent 标识时使用。
    /// </summary>
    public string? TargetAgentName { get; set; }

    /// <summary>
    /// Pet 为本次 dispatch 选定的首选 Provider ID。<c>null</c> 表示调用方尚未选择或该路径不需要 chat provider。
    /// </summary>
    public string? TargetProviderId { get; set; }

    /// <summary>
    /// Provider 回退候选 ID，不包含 <see cref="TargetProviderId"/>；空列表表示只尝试首选 Provider。
    /// <c>null</c> 表示尚未完成 provider 编排。
    /// </summary>
    public IReadOnlyList<string>? ProviderFallbackIds { get; set; }

    /// <summary>
    /// Pet 装配完成后交给 Agent 执行层的完整消息列表，包含 system / user / assistant / tool 等消息。
    /// <c>null</c> 表示尚未装配；空列表表示明确禁止向模型发送消息。
    /// </summary>
    public IReadOnlyList<ChatMessage>? AssembledMessages { get; set; }

    /// <summary>
    /// Pet 装配完成后交给 Agent 执行层的工具集合。<c>null</c> 表示尚未收集；空列表表示本轮不开放工具。
    /// </summary>
    public IReadOnlyList<AITool>? AssembledTools { get; set; }

    /// <summary>
    /// 在 <see cref="AssembledTools"/> 中仅供模型内部使用、不应直接展示给用户的工具名称集合。
    /// </summary>
    public IReadOnlySet<string>? InternalToolNames { get; set; }

    /// <summary>
    /// Pet 已解析完成的模型执行选项，包括模型 ID、采样参数、输出长度和工具调用模式等。
    /// </summary>
    public ChatOptions? ExecutionOptions { get; set; }

    /// <summary>
    /// Pet 为本次 dispatch 指定的 Temperature 覆盖。<c>null</c> 表示沿用执行层默认值。
    /// </summary>
    public float? TemperatureOverride { get; set; }

    /// <summary>
    /// Pet 为本次 dispatch 指定的 TopP 覆盖。<c>null</c> 表示沿用执行层默认值。
    /// </summary>
    public float? TopPOverride { get; set; }

    /// <summary>
    /// 本次 Agent 循环允许的最大工具迭代次数。<c>null</c> 表示执行层使用当前默认值。
    /// </summary>
    public int? MaxAgentIterations { get; set; }

    /// <summary>
    /// Prompt 装配阶段追加的行为提示后缀，通常来自 Pet 情绪/行为画像。装配完成后应体现在
    /// <see cref="AssembledMessages"/> 中。
    /// </summary>
    public string? PromptBehaviorSuffix { get; set; }

    /// <summary>
    /// Pet 私有知识或 RAG 检索结果。装配完成后应体现在 <see cref="AssembledMessages"/> 中。
    /// </summary>
    public string? PetKnowledge { get; set; }

    /// <summary>
    /// 是否已经完成本次 dispatch 的运行时子代理 ACL 裁决。
    /// 为 <c>false</c> 时，<see cref="RuntimeAllowedSubAgentIds"/> 的值不得被解释为最终许可。
    /// </summary>
    public bool HasRuntimeSubAgentAcl { get; set; }

    /// <summary>
    /// Pet 为本次 dispatch 裁决后的运行时子代理 ACL。仅当 <see cref="HasRuntimeSubAgentAcl"/> 为 <c>true</c> 时有效：
    /// null = 允许全部；空列表 = 禁止全部；具体 ID 列表 = 仅允许指定子代理。
    /// </summary>
    public IReadOnlyList<string>? RuntimeAllowedSubAgentIds { get; set; }

    /// <summary>
    /// 当前 Agent 调用祖先链，用于子代理循环调用防护。<c>null</c> 或空列表表示没有祖先代理。
    /// </summary>
    public IReadOnlyList<string>? AncestorAgentIds { get; set; }

    /// <summary>
    /// 本次 dispatch 的 assistant 最终消息；在 <see cref="MicroChatLifecyclePhase.AfterDispatch"/> 阶段开始前由编排器写入。
    /// 当前主对话路径会按流式输出聚合 token/thinking/data 内容并在成功收尾时写入；
    /// 没有 assistant 文本输出、走 Pet 直返路径、异常或取消时允许为 <c>null</c>。
    /// </summary>
    public SessionMessage? FinalAssistantMessage { get; set; }

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PreToolUse"/> 阶段表示正在调用的工具请求；其它阶段为 <c>null</c>。
    /// 当前主对话路径会依据流式 <see cref="ToolCallItem"/> 事件在进入该阶段前写入本字段。
    /// </summary>
    public ToolCallItem? CurrentToolCall { get; set; }

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PostToolUse"/>/<see cref="MicroChatLifecyclePhase.ToolUseFailure"/>
    /// 阶段表示最近一次工具调用的结果；其它阶段为 <c>null</c>。当前主对话路径会依据流式
    /// <see cref="ToolResultItem"/> 事件在对应阶段前写入本字段，并在阶段结束后恢复为空闲态。
    /// </summary>
    public ToolResultItem? LastToolResult { get; set; }

    /// <summary>
    /// 为系统级（非 Pet 对话流程）调用构造最小上下文：定时任务、RAG 审计、后台总结等。
    /// <para>
    /// <see cref="History"/>/<see cref="Pet"/>/<see cref="Channel"/>/<see cref="Output"/> 保持 <c>null</c>；
    /// Provider 会以 <see cref="Session"/>.Id 作为 usage 归属。
    /// </para>
    /// </summary>
    public static MicroChatContext ForSystem(
        IMicroSession session,
        string source,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new MicroChatContext
        {
            Session = session,
            Source = source,
            Ct = ct,
        };
    }

    /// <summary>
    /// 仅提供 <paramref name="sessionId"/> 的便捷重载：调用方（Pet 管线、Jobs）
    /// 没有手持 <see cref="IMicroSession"/> 聚合根时，用此构造一个最小 stub 仅用于
    /// <see cref="IUsageTracker"/> 归属。stub 上除 <c>Id</c> 以外的字段会抛
    /// <see cref="NotSupportedException"/>，强制上游在确需会话聚合根字段时改走
    /// <see cref="ForSystem(IMicroSession, string, CancellationToken)"/>。
    /// </summary>
    public static MicroChatContext ForSystem(
        string sessionId,
        string source,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new MicroChatContext
        {
            Session = new SystemSession(sessionId),
            Source = source,
            Ct = ct,
        };
    }

    /// <summary>
    /// 最小化的 <see cref="IMicroSession"/> stub，仅供 <see cref="ForSystem(string,string,CancellationToken)"/>
    /// 的系统调用场景使用：只透出 <see cref="Id"/>，其余字段默认值或抛 <see cref="NotSupportedException"/>。
    /// </summary>
    private sealed class SystemSession : IMicroSession
    {
        private readonly string _id;

        public SystemSession(string id) => _id = id;

        public string Id => _id;
        public string Title => string.Empty;
        public string ProviderId => string.Empty;
        public bool IsApproved => false;
        public ChannelType ChannelType => ChannelType.Web;
        public string ChannelId => string.Empty;
        public DateTimeOffset CreatedAt => default;
        public string? AgentId => null;
        public string? ApprovalReason => null;
        public IChannel? Channel => null;
        public IPet? Pet => null;

        public SessionEntity Entity =>
            throw new NotSupportedException(
                "System-created MicroChatContext does not back a real SessionEntity; use ForSystem(IMicroSession,...) if the caller needs the aggregate.");

        public SessionInfo ToInfo() =>
            throw new NotSupportedException(
                "System-created MicroChatContext does not back a real SessionInfo; use ForSystem(IMicroSession,...) if the caller needs it.");
        public IAsyncEnumerable<StreamItem> HandleMessageAsync(string content, IReadOnlyList<MessageAttachment>? attachments, string source, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
        
    }
}
