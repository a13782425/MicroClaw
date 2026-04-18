using System.Threading.Channels;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// 一次 Pet 对话的请求上下文：贯穿 <see cref="ChatLifecyclePhase"/> 四个阶段的载体。
/// <para>
/// 由 <c>MicroPet</c> 在消息处理入口构造，传给每个 <see cref="IChatLifecycleStep"/>。
/// 组件之间可以通过 <see cref="Items"/> 字典进行松耦合的数据交换；对特定阶段有明确语义的字段
/// （<see cref="FinalAssistantMessage"/> / <see cref="CurrentToolCall"/> / <see cref="LastToolResult"/>）
/// 在对应阶段之外可能为 <c>null</c>。
/// </para>
/// <para>
/// 本类型不是线程安全的——单次请求内通常由编排器串行驱动 steps，组件需要后台并行时自行同步。
/// </para>
/// </summary>
public sealed class ChatContext
{
    /// <summary>所属会话（Pet 宿主）。</summary>
    public required IMicroSession Session { get; init; }

    /// <summary>本次对话前已经加载的消息历史（含刚刚保存的用户消息，若调用方负责保存）。</summary>
    public required IReadOnlyList<SessionMessage> History { get; init; }

    /// <summary>
    /// 消息来源标签，常见值：<c>"chat"</c>（前端 API）、<c>"channel"</c>（渠道 webhook）、
    /// <c>"heartbeat"</c>（Pet 自主心跳触发，尚未启用）。
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 流式事件写入口；Step 可以直接 <c>Output.TryWrite(item)</c> 将事件挤到调用方的 async enumerator。
    /// <para>
    /// 当 Pet 未启用走直通 AgentRunner 分支时，<c>MicroPet</c> 当前版本不会为该分支构造 channel，
    /// 此字段会为 <c>null</c>；Step 实现需自行判空。Pet 启用分支由 <c>MicroPet</c> 在请求结束后负责 Complete。
    /// </para>
    /// </summary>
    public ChannelWriter<StreamItem>? Output { get; init; }

    /// <summary>贯穿整条链的取消令牌。</summary>
    public CancellationToken Ct { get; init; }

    /// <summary>组件间松耦合共享数据的扩展字典，键建议使用"<c>组件名:字段名</c>"命名空间。</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// 对话结束时的 assistant 最终消息；<see cref="ChatLifecyclePhase.AfterChat"/> 步骤开始前由编排器写入。
    /// 本版本 <c>MicroPet</c> 不累加 token，故本字段当前恒为 <c>null</c>；下一轮接入具体组件（如转发组件）
    /// 时再实现 token 聚合。
    /// </summary>
    public SessionMessage? FinalAssistantMessage { get; set; }

    /// <summary>
    /// <see cref="ChatLifecyclePhase.BeforeToolCall"/> 阶段表示正在调用的工具请求；其它阶段为 <c>null</c>。
    /// 本版本尚未接线到 <c>AgentRunner</c> 的工具钩子，该字段恒为 <c>null</c>。
    /// </summary>
    public ToolCallItem? CurrentToolCall { get; set; }

    /// <summary>
    /// <see cref="ChatLifecyclePhase.AfterToolCall"/> 阶段表示最近一次工具调用的结果；其它阶段为 <c>null</c>。
    /// 本版本尚未接线，该字段恒为 <c>null</c>。
    /// </summary>
    public ToolResultItem? LastToolResult { get; set; }
}
