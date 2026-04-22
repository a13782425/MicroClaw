
namespace MicroClaw.Configuration.Options;
public sealed record SessionEntity
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;
    
    [YamlMember(Alias = "title")]
    public string Title { get; set; } = string.Empty;
    
    [YamlMember(Alias = "provider_id")]
    public string ProviderId { get; set; } = string.Empty;
    
    [YamlMember(Alias = "is_approved")]
    public bool IsApproved { get; set; }
    
    [YamlMember(Alias = "channel_type")]
    public string ChannelType { get; set; } = "web";
    
    [YamlMember(Alias = "channel_id")]
    public string ChannelId { get; set; } = "web";
    
    [YamlMember(Alias = "created_at_ms")]
    public long CreatedAtMs { get; set; }
    
    [YamlMember(Alias = "agent_id")]
    public string? AgentId { get; set; }
    
    [YamlMember(Alias = "approval_reason")]
    public string? ApprovalReason { get; set; }
    
    public SessionEntity DeepClone() =>
        new()
        {
            Id = this.Id,
            Title = this.Title,
            ProviderId = this.ProviderId,
            IsApproved = this.IsApproved,
            ChannelType = this.ChannelType,
            ChannelId = this.ChannelId,
            CreatedAtMs = this.CreatedAtMs,
            AgentId = this.AgentId,
            ApprovalReason = this.ApprovalReason,
        };
}