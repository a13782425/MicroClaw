namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// Per-Session Pet 上下文的生命周期状态。
/// </summary>
public enum PetContextState
{
    /// <summary>尚未初始化（Pet 目录未创建、或 PetFactory 尚未调用前的默认状态）。</summary>
    Uninitialized,

    /// <summary>已激活，正常参与消息编排。</summary>
    Active,

    /// <summary>已禁用（Session 被禁用，或 PetConfig.Enabled = false）。</summary>
    Disabled,
}

/// <summary>
/// Per-Session Pet 上下文的抽象接口。
/// <para>
/// 定义在 <c>MicroClaw.Abstractions</c> 层，避免会话模型直接引用
/// <c>PetContext</c>（定义于 MicroClaw.Pet）而产生具体实现耦合。
/// 实现类为 <c>MicroClaw.Pet.PetContext</c>。
/// </para>
/// </summary>
public interface IPetContext : IPet
{
    /// <summary>当前 Pet 生命周期状态。</summary>
    PetContextState State { get; }

    /// <summary>
    /// Pet 是否处于可编排状态（<see cref="PetContextState.Active"/> 且 PetConfig.Enabled = true）。
    /// </summary>
    new bool IsEnabled { get; }

    /// <summary>
    /// 将本 PetContext 标记为"脏"，提示持久化层在下次合适时机将状态回写磁盘。
    /// 线程安全。
    /// </summary>
    void MarkDirty();
}
