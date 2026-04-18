using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Utils;

namespace MicroClaw.Sessions;
/// <summary>
/// Session 聚合根（领域对象）。
/// <para>
/// 继承 <see cref="MicroObject"/> 以接入 MicroClaw.Core 的组件模式：消息持久化等职能
/// 作为 <see cref="MicroComponent"/> 附着到会话上。构造保持 <c>private</c>，外部只能通过
/// <see cref="CreateAsync"/>  工厂创建。
/// </para>
/// <para>
/// 使用 <c>class</c> 而非 <c>record</c>，确保以引用相等性（identity equality）判定同一会话，
/// 避免两个属性不同但 Id 相同的实例被视为“不等”。
/// </para>
/// <para>
/// 所有属性均为 <c>private set</c>，外部状态变更只能通过行为方法（Approve/Disable/…）进行。
/// </para>
/// </summary>
public class MicroSession : MicroObject, IMicroSession
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
    
    /// <summary>
    /// 消息持久化组件快捷访问。组件在 <see cref="MicroSession"/> 创建后由
    /// <see cref="SessionService"/> 显式挂接；尚未挂接时访问本属性会抛出异常。
    /// </summary>
    public SessionMessagesComponent Messages => GetComponent<SessionMessagesComponent>() ?? throw new InvalidOperationException($"Session '{Id}' does not have {nameof(SessionMessagesComponent)} attached yet.");
    
    public static async Task<MicroSession> CreateAsync(SessionEntity entity, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        MicroSession session = new(entity);
        await session.AddComponentAsync<SessionMessagesComponent>(cancellationToken);
        session.Pet = await serviceProvider.GetRequiredService<IPetFactory>()!.CreateOrLoadAsync(session, cancellationToken);
        return session;
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
    
    public SessionInfo ToInfo() => new(Id, Title, ProviderId, IsApproved, ChannelType, ChannelId, CreatedAt, AgentId, ApprovalReason);
}