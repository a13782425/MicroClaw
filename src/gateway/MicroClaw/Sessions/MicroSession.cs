using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
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
    private readonly List<IDomainEvent> _domainEvents = [];

    private MicroSession() { }

    public string Id { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string ProviderId { get; private set; } = string.Empty;
    public bool IsApproved { get; private set; }
    public ChannelType ChannelType { get; private set; }
    public string ChannelId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? AgentId { get; private set; }
    public string? ApprovalReason { get; private set; }
    public IChannel? Channel { get; private set; }
    public IPet? Pet { get; private set; }

    public static MicroSession Reconstitute(
        string id,
        string title,
        string providerId,
        bool isApproved,
        ChannelType channelType,
        string channelId,
        DateTimeOffset createdAt,
        string? agentId = null,
        string? approvalReason = null)
    {
        return new MicroSession
        {
            Id = id,
            Title = title,
            ProviderId = providerId,
            IsApproved = isApproved,
            ChannelType = channelType,
            ChannelId = channelId,
            CreatedAt = createdAt,
            AgentId = agentId,
            ApprovalReason = approvalReason,
        };
    }

    public static MicroSession Create(
        string id,
        string title,
        string providerId,
        ChannelType channelType,
        string channelId,
        DateTimeOffset createdAt,
        string? agentId = null)
    {
        return new MicroSession
        {
            Id = id,
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            ChannelType = channelType,
            ChannelId = channelId,
            CreatedAt = createdAt,
            AgentId = agentId,
        };
    }

    public void Approve(string? reason = null)
    {
        IsApproved = true;
        ApprovalReason = reason;
        RaiseDomainEvent(new SessionApprovedEvent(Id));
    }

    public void Disable(string? reason = null)
    {
        IsApproved = false;
        ApprovalReason = reason;
    }

    public void UpdateProvider(string newProviderId)
    {
        string old = ProviderId;
        ProviderId = newProviderId;
        RaiseDomainEvent(new SessionProviderChangedEvent(Id, old, newProviderId));
    }

    public void UpdateTitle(string newTitle)
    {
        Title = newTitle;
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

    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList().AsReadOnly();
        _domainEvents.Clear();
        return events;
    }

    private void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public SessionInfo ToInfo() => new(
        Id, Title, ProviderId, IsApproved,
        ChannelType, ChannelId, CreatedAt,
        AgentId, ApprovalReason);
}
