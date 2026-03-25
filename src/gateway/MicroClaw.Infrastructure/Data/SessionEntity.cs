namespace MicroClaw.Infrastructure.Data;

public sealed class SessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public string ChannelType { get; set; } = "web";
    public string ChannelId { get; set; } = "web";
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
    public string? AgentId { get; set; }
    public string? ParentSessionId { get; set; }
    public string? ApprovalReason { get; set; }
}
