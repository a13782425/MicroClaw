using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Pet;

namespace MicroClaw.Abstractions.Sessions;

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
public sealed class Session
{
    // ── 维护待分发的领域事件队列 ──────────────────────────────────────────────
    private readonly List<IDomainEvent> _domainEvents = [];

    private Session() { }  // 防止外部直接 new

    // ── 属性（只读外部）──────────────────────────────────────────────────────

    public string Id { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string ProviderId { get; private set; } = string.Empty;
    public bool IsApproved { get; private set; }
    public ChannelType ChannelType { get; private set; }
    public string ChannelId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? AgentId { get; private set; }
    public string? ParentSessionId { get; private set; }
    public string? ApprovalReason { get; private set; }

    /// <summary>当前 Session 关联的 Per-Session Pet 上下文。null 表示 Pet 尚未初始化。</summary>
    public IPetContext? PetContext { get; private set; }

    // ── 工厂方法 ────────────────────────────────────────────────────────────

    /// <summary>从持久化数据重建 Session 实例（不触发任何领域事件）。</summary>
    public static Session Reconstitute(
        string id,
        string title,
        string providerId,
        bool isApproved,
        ChannelType channelType,
        string channelId,
        DateTimeOffset createdAt,
        string? agentId = null,
        string? parentSessionId = null,
        string? approvalReason = null)
    {
        return new Session
        {
            Id = id,
            Title = title,
            ProviderId = providerId,
            IsApproved = isApproved,
            ChannelType = channelType,
            ChannelId = channelId,
            CreatedAt = createdAt,
            AgentId = agentId,
            ParentSessionId = parentSessionId,
            ApprovalReason = approvalReason,
        };
    }

    /// <summary>创建全新 Session（不触发任何领域事件，由存储层负责持久化后发布）。</summary>
    public static Session Create(
        string id,
        string title,
        string providerId,
        ChannelType channelType,
        string channelId,
        DateTimeOffset createdAt,
        string? agentId = null,
        string? parentSessionId = null)
    {
        return new Session
        {
            Id = id,
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            ChannelType = channelType,
            ChannelId = channelId,
            CreatedAt = createdAt,
            AgentId = agentId,
            ParentSessionId = parentSessionId,
        };
    }

    // ── 行为方法 ─────────────────────────────────────────────────────────────

    /// <summary>审批会话，并发布 <see cref="SessionApprovedEvent"/>。</summary>
    public void Approve(string? reason = null)
    {
        IsApproved = true;
        ApprovalReason = reason;
        RaiseDomainEvent(new SessionApprovedEvent(Id));
    }

    /// <summary>禁用会话（撤销审批），不发布领域事件（禁用属于管理操作，下游无特殊处理）。</summary>
    public void Disable(string? reason = null)
    {
        IsApproved = false;
        ApprovalReason = reason;
    }

    /// <summary>更新关联的 Provider，并发布 <see cref="SessionProviderChangedEvent"/>。</summary>
    public void UpdateProvider(string newProviderId)
    {
        string old = ProviderId;
        ProviderId = newProviderId;
        RaiseDomainEvent(new SessionProviderChangedEvent(Id, old, newProviderId));
    }

    /// <summary>更新会话标题（暂无下游事件需求，仅改属性）。</summary>
    public void UpdateTitle(string newTitle)
    {
        Title = newTitle;
    }

    // ── Pet 关联 ──────────────────────────────────────────────────────────────

    /// <summary>附加 Pet 上下文（审批后由 PetFactory 调用）。</summary>
    public void AttachPet(IPetContext ctx)
    {
        PetContext = ctx;
    }

    /// <summary>分离 Pet 上下文（Session 删除流程中调用）。</summary>
    public void DetachPet()
    {
        PetContext = null;
    }

    // ── 领域事件 ──────────────────────────────────────────────────────────────

    /// <summary>获取并清除所有待分发的领域事件（由存储层或应用服务在持久化后调用）。</summary>
    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList().AsReadOnly();
        _domainEvents.Clear();
        return events;
    }

    private void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    // ── DTO 转换 ──────────────────────────────────────────────────────────────

    /// <summary>将领域对象转换为 API 层 DTO（向后兼容，前端无需改动）。</summary>
    public SessionInfo ToInfo() => new(
        Id, Title, ProviderId, IsApproved,
        ChannelType, ChannelId, CreatedAt,
        AgentId, ParentSessionId, ApprovalReason);
}
