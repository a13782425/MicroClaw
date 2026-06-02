using MicroClaw.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 江湖中一切"活实体"的共同基类：世界、门派、弟子、侠客。
/// <para>
/// 继承 <see cref="MicroObject"/> 接入 Core 的组件模式与生命周期；在其之上强制两条不变量：
/// 1) 每个对象都有稳定领域 <see cref="Id"/>（区别于 Core 的 <c>InstanceId</c>）；
/// 2) 每个对象都归属某一方 <see cref="WorldId"/>，防止跨江湖串账。
/// </para>
/// <para>
/// 使用 <c>class</c> 而非 <c>record</c>：以引用/Id 相等判定同一实体，避免字段不同但同 Id 被判不等。
/// </para>
/// </summary>
public abstract class MicroGameObject : MicroObject, IEquatable<MicroGameObject>
{
    /// <param name="id">稳定领域 Id；为空时由调用方在工厂里生成。</param>
    /// <param name="worldId">所属江湖 Id。世界对象自身的 WorldId 等于其 Id。</param>
    protected MicroGameObject(string id, string worldId)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("江湖对象必须有非空 Id。", nameof(id));
        if (string.IsNullOrWhiteSpace(worldId))
            throw new ArgumentException("江湖对象必须归属某个 WorldId。", nameof(worldId));
        Id = id;
        WorldId = worldId;
    }
    /// <summary>
    /// 稳定领域标识（持久化主键），区别于基类的运行时 <see cref="MicroLifeCycle{T}.InstanceId"/>。
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// 所属江湖。强制隔离，防止跨世界引用。
    /// </summary>
    public string WorldId { get; }
    /// <summary>
    /// 对象名字
    /// </summary>
    public virtual string Name { get; set; } = string.Empty;

    public override string InstanceId => Id;

    public bool Equals(MicroGameObject? other) => other is not null && GetType() == other.GetType() && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as MicroGameObject);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public override string ToString() => $"{GetType().Name}({Id}@{WorldId})";
}
