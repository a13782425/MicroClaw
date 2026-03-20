namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Token 使用量记录，快照价格保证历史费用计算准确。
/// </summary>
public sealed class UsageEntity
{
    public int Id { get; set; }

    /// <summary>关联会话 ID（子代理调用、渠道消息等场景可能为 null）。</summary>
    public string? SessionId { get; set; }

    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>调用来源：chat / cron / channel / subagent</summary>
    public string Source { get; set; } = string.Empty;

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>记录时快照的输入单价（USD/1M tokens），可 null 表示未配置。</summary>
    public decimal? InputPricePerMToken { get; set; }

    /// <summary>记录时快照的输出单价（USD/1M tokens），可 null 表示未配置。</summary>
    public decimal? OutputPricePerMToken { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
