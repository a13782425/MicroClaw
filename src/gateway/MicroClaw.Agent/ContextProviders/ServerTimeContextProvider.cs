using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// 服务器时间上下文提供者：将当前服务器本地时间�?UTC 时间注入 System Prompt�?
/// 避免 AI 在处理时间相关任务（如创建定时任务）时需要额外调�?get_current_time 工具�?
/// </summary>
public sealed class ServerTimeContextProvider : IAgentContextProvider
{
    /// <inheritdoc />
    /// <remarks>Order 5：在所有其�?Provider 之前注入，作为基础时间参考层�?/remarks>
    public int Order => 5;

    /// <inheritdoc />
    public ValueTask<string?> BuildContextAsync(Agent agent, string? sessionId, CancellationToken ct = default)
    {
        string localTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        string utcTime = DateTimeOffset.UtcNow.ToString("O");
        string context = $"当前服务器时间：{localTime}（UTC: {utcTime}）";
        return ValueTask.FromResult<string?>(context);
    }
}
