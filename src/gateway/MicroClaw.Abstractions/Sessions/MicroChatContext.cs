using System.Threading.Channels;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration.Options;

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
    /// 本次 dispatch 的 assistant 最终消息；在 <see cref="MicroChatLifecyclePhase.AfterDispatch"/> 阶段开始前由编排器写入。
    /// 本版本 <c>MicroPet</c> 不累加 token，故本字段当前恒为 <c>null</c>；下一轮接入具体组件（如转发组件）
    /// 时再实现 token 聚合。
    /// </summary>
    public SessionMessage? FinalAssistantMessage { get; set; }

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PreToolUse"/> 阶段表示正在调用的工具请求；其它阶段为 <c>null</c>。
    /// 本版本尚未接线到 <c>AgentRunner</c> 的工具钩子，该字段恒为 <c>null</c>。
    /// </summary>
    public ToolCallItem? CurrentToolCall { get; set; }

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PostToolUse"/>/<see cref="MicroChatLifecyclePhase.ToolUseFailure"/>
    /// 阶段表示最近一次工具调用的结果；其它阶段为 <c>null</c>。本版本尚未接线，该字段恒为 <c>null</c>。
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
    }
}
