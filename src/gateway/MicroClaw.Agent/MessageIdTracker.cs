namespace MicroClaw.Agent;

/// <summary>
/// 在流式 ReAct 循环中共享当前 turn 的 MessageId。
/// 由 AgentRunner 的流式循环从 <c>AgentResponseUpdate.MessageId</c> 设值，
/// FunctionInvoker 闭包读取以关联工具调用事件到同一 turn。
/// <para>
/// 线程安全说明：RunStreamingAsync 的 token 生成和 FunctionInvoker 回调在同一异步迭代内串行执行，
/// FunctionInvokingChatClient 阻塞 token 生成直到工具执行完毕，因此不存在并发写入。
/// </para>
/// </summary>
internal sealed class MessageIdTracker
{
    /// <summary>当前 turn 的消息 ID。</summary>
    public string? Current { get; set; }
}
