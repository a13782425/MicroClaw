using MicroClaw.Agent.Middleware;
using MicroClaw.Gateway.Contracts.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Streaming.Handlers;

/// <summary>
/// 处理 <see cref="UsageContent"/>，捕获 Usage 指标但不产生 StreamItem。
/// 使用 AsyncLocal 存储每次请求的 <see cref="UsageCapture"/>，线程安全于并发请求。
/// </summary>
public sealed class UsageContentHandler : IAIContentHandler
{
    private static readonly AsyncLocal<UsageCapture?> _currentCapture = new();

    /// <summary>获取当前请求绑定的 UsageCapture。</summary>
    public static UsageCapture? CurrentCapture => _currentCapture.Value;

    /// <summary>绑定一个 UsageCapture 到当前异步上下文（每次 StreamReActAsync 调用前设置）。</summary>
    public static void BindCapture(UsageCapture capture) => _currentCapture.Value = capture;

    /// <summary>解除当前异步上下文的 UsageCapture 绑定。</summary>
    public static void UnbindCapture() => _currentCapture.Value = null;

    public bool CanHandle(AIContent content) => content is UsageContent;

    public StreamItem? Convert(AIContent content)
    {
        var uc = (UsageContent)content;
        if (_currentCapture.Value is { } capture)
            capture.LastUsage = uc.Details;
        return null; // Usage 仅用于内部指标，不产生流事件
    }
}
