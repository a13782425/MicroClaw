using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;
/// <summary>
/// Session runtime contract exposed across module boundaries.
/// </summary>
public interface IMicroSession
{
    string Id { get; }
    string Title { get; }
    string ProviderId { get; }
    bool IsApproved { get; }
    ChannelType ChannelType { get; }
    string ChannelId { get; }
    SessionEntity Entity { get; }
    DateTimeOffset CreatedAt { get; }
    string? AgentId { get; }
    string? ApprovalReason { get; }
    IChannel? Channel { get; }
    IPet? Pet { get; }
    SessionInfo ToInfo();
    
    /// <summary>
    /// 处理一条入站用户消息的完整生命周期：
    ///   1. 持久化用户消息
    ///   2. 加载历史上下文
    ///   3. 驱动 Pet → AgentRunner 执行 ReAct 循环
    ///   4. 持久化 assistant 回复
    ///   5. 流式 yield 全部 StreamItem，供调用方决定如何消费（SSE / 物化 / 忽略）
    /// <para>
    /// 渠道层只负责协议解析、去重、审批检查、发送回复；
    /// 会话层完全封装对话逻辑，调用方无需知道 Pet / AgentRunner / 历史加载。
    /// </para>
    /// </summary>
    IAsyncEnumerable<StreamItem> HandleMessageAsync(string content, IReadOnlyList<MessageAttachment>? attachments, string source, CancellationToken ct = default);
}