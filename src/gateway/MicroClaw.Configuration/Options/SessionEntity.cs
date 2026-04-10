using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration.Options;
public sealed record SessionEntity
{
    [ConfigurationKeyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [ConfigurationKeyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [ConfigurationKeyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;
    
    [ConfigurationKeyName("is_approved")]
    public bool IsApproved { get; set; }
    
    [ConfigurationKeyName("channel_type")]
    public string ChannelType { get; set; } = "web";
    
    [ConfigurationKeyName("channel_id")]
    public string ChannelId { get; set; } = "web";
    
    [ConfigurationKeyName("created_at_ms")]
    public long CreatedAtMs { get; set; }
    
    [ConfigurationKeyName("agent_id")]
    public string? AgentId { get; set; }
    
    [ConfigurationKeyName("approval_reason")]
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