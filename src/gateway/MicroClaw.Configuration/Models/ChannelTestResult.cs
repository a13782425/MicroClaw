namespace MicroClaw.Configuration.Models;

/// <summary>渠道连通性测试结果。</summary>
/// <param name="ConnectivityHint">
/// 可选的连通性提示（如 Webhook 模式判断为内网环境时，建议切换 WebSocket）。
/// null 表示无额外提示。
/// </param>
public sealed record ChannelTestResult(
    bool Success,
    string Message,
    long LatencyMs,
    string? ConnectivityHint = null);
