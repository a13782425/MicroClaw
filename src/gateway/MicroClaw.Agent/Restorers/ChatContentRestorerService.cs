using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>
/// SessionMessage → AIContent 还原服务。内部聚合所有 <see cref="IChatContentRestorer"/> 实现，
/// 按注册顺序匹配并还原。未匹配时回退为 <see cref="TextContent"/>。
/// </summary>
public sealed class ChatContentRestorerService
{
    private readonly IReadOnlyList<IChatContentRestorer> _restorers = new IChatContentRestorer[]
    {
        new ThinkingContentRestorer(),
        new TextContentRestorer(),
        new FunctionCallRestorer(),
        new FunctionResultRestorer(),
        new DataContentRestorer(),
    };

    /// <summary>通过注册的 Restorer 将 SessionMessage 还原为 AIContent 列表。</summary>
    public List<AIContent> RestoreContents(SessionMessage msg)
    {
        var contents = new List<AIContent>();
        foreach (IChatContentRestorer restorer in _restorers)
        {
            if (restorer.CanRestore(msg))
                contents.AddRange(restorer.Restore(msg));
        }

        // 如果没有任何 Restorer 匹配但有内容，回退为 TextContent
        if (contents.Count == 0 && !string.IsNullOrEmpty(msg.Content))
            contents.Add(new TextContent(msg.Content));

        return contents;
    }
}
