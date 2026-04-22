
namespace MicroClaw.Configuration.Options;

/// <summary>
/// 会话元数据持久化实体。
/// </summary>
public sealed record SessionEntity
{
    /// <summary>
    /// 会话唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "会话唯一标识。")]
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话标题。
    /// </summary>
    [YamlMember(Alias = "title", Description = "会话标题。")]
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// 当前会话绑定的 Provider ID。
    /// </summary>
    [YamlMember(Alias = "provider_id", Description = "当前会话绑定的 Provider ID。")]
    public string ProviderId { get; set; } = string.Empty;
    
    /// <summary>
    /// 指示该会话是否已经通过审批。
    /// </summary>
    [YamlMember(Alias = "is_approved", Description = "指示该会话是否已经通过审批。")]
    public bool IsApproved { get; set; }
    
    /// <summary>
    /// 会话来源的渠道类型。
    /// </summary>
    [YamlMember(Alias = "channel_type", Description = "会话来源的渠道类型。")]
    public string ChannelType { get; set; } = "web";
    
    /// <summary>
    /// 会话来源的具体渠道实例 ID。
    /// </summary>
    [YamlMember(Alias = "channel_id", Description = "会话来源的具体渠道实例 ID。")]
    public string ChannelId { get; set; } = "web";
    
    /// <summary>
    /// 创建时间的 Unix 毫秒时间戳。
    /// </summary>
    [YamlMember(Alias = "created_at_ms", Description = "创建时间的 Unix 毫秒时间戳。")]
    public long CreatedAtMs { get; set; }
    
    /// <summary>
    /// 当前会话绑定的 Agent ID。
    /// </summary>
    [YamlMember(Alias = "agent_id", Description = "当前会话绑定的 Agent ID。")]
    public string? AgentId { get; set; }
    
    /// <summary>
    /// 审批原因或备注。
    /// </summary>
    [YamlMember(Alias = "approval_reason", Description = "审批原因或备注。")]
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