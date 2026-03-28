using System.Threading.Channels;

namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>
/// 通过 <see cref="AsyncLocal{T}"/> 在父 Agent 工具调用上下文和子代理运行器之间传递事件 Channel。
/// <para>
/// 父 Agent 的 <c>BuildFunctionInvoker</c> 在调用工具前设置 <see cref="Current"/>，
/// 子代理运行器读取该值以将进度事件（<see cref="SubAgentStartItem"/>、<see cref="SubAgentProgressItem"/>、
/// <see cref="SubAgentResultItem"/>）写入父 SSE 流，避免子代理执行期间前端 SSE 完全静默。
/// </para>
/// </summary>
public static class SubAgentEventBridge
{
    private static readonly AsyncLocal<ChannelWriter<StreamItem>?> _writer = new();

    /// <summary>获取或设置当前异步上下文中的父事件 Writer。</summary>
    public static ChannelWriter<StreamItem>? Current
    {
        get => _writer.Value;
        set => _writer.Value = value;
    }
}
