namespace MicroClaw.Abstractions.Events;

/// <summary>
/// 领域事件标记接口。所有领域事件须实现此接口。
/// <para>
/// 设计为标记接口（无成员），仅用于类型约束与分发路由，
/// 具体事件通过 <c>record</c> 承载不可变数据。
/// </para>
/// </summary>
public interface IDomainEvent { }
