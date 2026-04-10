using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration.Options;
using MicroClaw.Utils;

namespace MicroClaw.Sessions;
/// <summary>
/// Session 聚合根（领域对象）。
/// <para>
/// 使用 <c>class</c> 而非 <c>record</c>，确保以引用相等性（identity equality）判定同一会话，
/// 避免两个属性不同但 Id 相同的实例被视为"不等"。
/// </para>
/// <para>
/// 所有属性均为 <c>private set</c>，外部状态变更只能通过行为方法（Approve/Disable/…）进行。
/// </para>
/// </summary>
public sealed class MicroSession : IMicroSession
{
    
    private MicroSession(SessionEntity entity)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
    }
    public SessionEntity Entity { get; private set; }
    
    public string Id => Entity.Id;
    public string Title => Entity.Title;
    public string ProviderId => Entity.ProviderId;
    public bool IsApproved => Entity.IsApproved;
    public ChannelType ChannelType => ChannelService.ParseChannelType(Entity.ChannelType);
    public string ChannelId => string.IsNullOrEmpty(Entity.ChannelId) ? ChannelService.WebChannelId : Entity.ChannelId;
    public DateTimeOffset CreatedAt => TimeUtils.FromMs(Entity.CreatedAtMs);
    public string? AgentId => Entity.AgentId;
    public string? ApprovalReason => Entity.ApprovalReason;
    public IChannel? Channel { get; private set; }
    public IPet? Pet { get; private set; }
    public static MicroSession Reconstitute(SessionEntity entity) => new(entity);
    
    public static MicroSession Create(string id, string title, string providerId, ChannelType channelType, string channelId, DateTimeOffset createdAt, string? agentId = null)
    {
        var entity = new SessionEntity
        {
            Id = id,
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            ChannelType = ChannelService.SerializeChannelType(channelType),
            ChannelId = channelId,
            CreatedAtMs = TimeUtils.ToMs(createdAt),
            AgentId = agentId,
        };
        return new MicroSession(entity);
    }
    
    public void Approve(string? reason = null)
    {
        Entity.IsApproved = true;
        Entity.ApprovalReason = reason;
    }
    
    public void Disable(string? reason = null)
    {
        Entity.IsApproved = false;
        Entity.ApprovalReason = reason;
    }
    
    public void UpdateProvider(string newProviderId)
    {
        Entity.ProviderId = newProviderId;
    }
    
    public void UpdateTitle(string newTitle)
    {
        Entity.Title = newTitle;
    }
    
    public void AttachChannel(IChannel channel)
    {
        Channel = channel;
    }
    
    public void AttachPet(IPet pet)
    {
        Pet = pet;
    }
    
    public void DetachPet()
    {
        Pet = null;
    }
    
    public SessionInfo ToInfo() => new(Id, Title, ProviderId, IsApproved, ChannelType, ChannelId, CreatedAt, AgentId, ApprovalReason);
}