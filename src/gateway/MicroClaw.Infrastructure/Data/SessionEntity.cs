namespace MicroClaw.Infrastructure.Data;

public sealed class SessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public string ChannelType { get; set; } = "web";
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string? ParentSessionId { get; set; }
}
