using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>
/// 将 <see cref="SessionMessage"/> 还原为 MEAI <see cref="AIContent"/> 的策略接口。
/// 多个 Restorer 可同时匹配同一消息（如同时有 ThinkContent 和 Text），
/// 由 BuildChatMessagesAsync 依次收集所有输出的 AIContent。
/// </summary>
public interface IChatContentRestorer
{
    /// <summary>判断是否能从该消息还原出 AIContent。</summary>
    bool CanRestore(SessionMessage message);

    /// <summary>从消息中还原出一个或多个 AIContent。</summary>
    IEnumerable<AIContent> Restore(SessionMessage message);
}
