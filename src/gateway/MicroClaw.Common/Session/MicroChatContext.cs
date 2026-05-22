using Microsoft.Extensions.AI;
namespace MicroClaw.Common;
public sealed class MicroChatContext
{
    /// <summary>
    /// 所属会话
    /// </summary>
    public required IMicroSession Session { get; init; }
    
    /// <summary>
    /// 消息来源标签，常见值：<c>"chat"</c>（前端 API）、<c>"channel"</c>（渠道 webhook）、
    /// <c>"heartbeat"</c>（Pet 自主心跳触发）、<c>"rag-audit"</c>（RAG 审计）、<c>"dreaming"</c>/<c>"memory-summary"</c> 等。
    /// </summary>
    public required string Source { get; init; }
    
    /// <summary>贯穿整条链的取消令牌。</summary>
    public CancellationToken Ct { get; init; }
    /// <summary>
    /// 本次对话前已经加载的消息历史（含刚刚保存的用户消息，由调用方负责保存）。
    /// </summary>
    public IReadOnlyList<ChatMessage>? Messages { get; set; }
    /// <summary>
    /// 当前对话的工具列表。由调用方负责装配
    /// </summary>
    public IReadOnlyList<AITool> Tools  { get; set; } = [];
    /// <summary>
    /// 本次 Agent 循环允许的最大工具迭代次数。<c>null</c> 表示执行层使用当前默认值。
    /// </summary>
    public int MaxToolIterations { get; set; } = 10;
}