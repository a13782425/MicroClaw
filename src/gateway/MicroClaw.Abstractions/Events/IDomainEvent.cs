namespace MicroClaw.Abstractions.Events;

/// <summary>
/// 领域事件标记接口。
/// </summary>
/// <remarks>
/// 已过时，请改用 <see cref="IAsyncEventBus"/> 发布普通 record 事件。
/// 现有用法（SessionApprovedEvent / SessionDeletedEvent）在迁移前暂时保留。
/// </remarks>
[Obsolete("请改用 IAsyncEventBus 发布/订阅事件，IDomainEvent 体系将在后续版本移除。")]
public interface IDomainEvent { }
