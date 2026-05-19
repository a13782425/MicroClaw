using System.Runtime.CompilerServices;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Channels;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Pet;
using MicroClaw.Sessions.Components;
using MicroClaw.Streaming;
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
    private readonly IServiceProvider _serviceProvider;

    private MicroSession(SessionEntityConfig entityConfig, IServiceProvider serviceProvider)
    {
        EntityConfig = entityConfig ?? throw new ArgumentNullException(nameof(entityConfig));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    public SessionEntityConfig EntityConfig { get; private set; }
    
    public string Id => EntityConfig.Id;
    public string Title => EntityConfig.Title;
    public string ProviderId => EntityConfig.ProviderId;
    public bool IsApproved => EntityConfig.IsApproved;
    public ChannelType ChannelType => ChannelUtils.ParseChannelType(EntityConfig.ChannelType);
    public string ChannelId => string.IsNullOrEmpty(EntityConfig.ChannelId) ? ChannelUtils.WebChannelId : EntityConfig.ChannelId;
    public DateTimeOffset CreatedAt => TimeUtils.FromMs(EntityConfig.CreatedAtMs);
    public string? AgentId => EntityConfig.AgentId;
    public string? ApprovalReason => EntityConfig.ApprovalReason;
    public IChannel? Channel { get; private set; }
    public IPet? Pet { get; private set; }
    
    /// <summary>
    /// 消息持久化组件快捷访问。组件在 <see cref="MicroSession"/> 创建后由
    /// <see cref="SessionService"/> 显式挂接；尚未挂接时访问本属性会抛出异常。
    /// </summary>
    public SessionMessagesComponent Messages => GetComponent<SessionMessagesComponent>() ?? throw new InvalidOperationException($"Session '{Id}' does not have {nameof(SessionMessagesComponent)} attached yet.");
    
    public static async Task<MicroSession> CreateAsync(SessionEntityConfig entityConfig, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        MicroSession session = new(entityConfig, serviceProvider);
        try
        {
            await session.AddComponentAsync<SessionMessagesComponent>(cancellationToken);
            session.Pet = await serviceProvider.GetRequiredService<PetService>().CreateOrLoadAsync(session, cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
    
    public void Approve(string? reason = null)
    {
        EntityConfig.IsApproved = true;
        EntityConfig.ApprovalReason = reason ?? "";
    }
    
    public void Disable(string? reason = null)
    {
        EntityConfig.IsApproved = false;
        EntityConfig.ApprovalReason = reason ?? "";
    }
    
    public void UpdateProvider(string newProviderId)
    {
        EntityConfig.ProviderId = newProviderId;
    }
    
    public void UpdateTitle(string newTitle)
    {
        EntityConfig.Title = newTitle;
    }
    
    public SessionInfo ToInfo() => new(Id, Title, ProviderId, IsApproved, ChannelType, ChannelId, CreatedAt, AgentId, ApprovalReason);
    
    #region 接口实现
    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(string content, IReadOnlyList<MessageAttachment>? attachments, string source, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsApproved)
        {
            yield return new ErrorItem("会话尚未获得批准，请联系管理员。");
            yield break;
        }

        Pet = await _serviceProvider.GetRequiredService<PetService>().ActivateAsync(this, ct);
        
        if (Pet is null)
        {
            yield return new ErrorItem("宠物在这个会话没有启用。");
            yield break;
        }
        
        // 1. 持久化用户消息
        SessionMessage userMessage = new(Id: MicroClawUtils.GetUniqueId(), Role: "user", Content: content, ThinkContent: null, Timestamp: TimeUtils.NowOffset(), Attachments: attachments, Source: source);
        Messages.AddMessage(userMessage);
        
        
        // 3. 加载完整历史（含刚才写入的用户消息）
        IReadOnlyList<SessionMessage> history = Messages.GetMessages();
        
        // 4. 流式执行 + 同步持久化 assistant 消息
        var pipeline = new StreamItemPersistencePipeline();
        await foreach (StreamItem item in Pet.HandleMessageAsync(history, ct, source))
        {
            // 工具调用 / 子代理等立即产生消息的类型，直接入库
            foreach (SessionMessage msg in pipeline.ProcessItem(item))
                Messages.AddMessage(msg);
            
            yield return item;
        }
        
        // 5. 聚合最终 assistant 消息（文本 + think + 附件）
        SessionMessage? assistantMsg = pipeline.Finalize();
        if (assistantMsg is not null)
            Messages.AddMessage(assistantMsg);
    }
    #endregion
    
}