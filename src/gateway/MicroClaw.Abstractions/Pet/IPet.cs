using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// 标记接口：表示一个与 Session 关联的宠物实体。
/// <para>
/// 定义在 <c>MicroClaw.Abstractions</c> 层，使 <see cref="Sessions.Session"/> 可持有宠物引用
/// 而不产生循环依赖。实现类为 <c>MicroClaw.Pet.MicroPet</c>。
/// </para>
/// </summary>
public interface IPet
{
    /// <summary>当前 Pet 绑定的会话。</summary>
    IMicroSession MicroSession { get; }

    /// <summary>宠物是否处于可参与编排的激活状态。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 处理用户聊天消息。Pet 内部完成：保存用户消息、加载历史、解析 Provider/Agent、
    /// 执行决策和 AgentRunner 调用，返回流式事件。
    /// </summary>
    IAsyncEnumerable<StreamItem> HandleChatAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 处理渠道消息。调用方已保存用户消息并加载历史，Pet 仅负责 Provider/Agent 解析和执行。
    /// </summary>
    IAsyncEnumerable<StreamItem> HandleMessageAsync(
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default,
        string source = "chat");

    /// <summary>
    /// Collect channel-specific AI tools via the bound session's channel.
    /// Default implementation returns an empty list.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CollectChannelToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AIFunction>>([]);
}
