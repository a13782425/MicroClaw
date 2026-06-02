namespace MicroClaw.Core;

/// <summary>
/// 组件：挂在 <see cref="MicroObject"/> 上提供具体行为，真正实现各生命周期钩子。
/// 生命周期由所属 obj 转发驱动（同一 obj 内先全员 OnAwake、再全员 OnStart）。
/// </summary>
public abstract class MicroComponent : MicroLifecycle
{
    /// <summary>所属对象（Owner）；未挂载时为 null。由 <see cref="MicroObject"/> 在挂载/卸载时维护。</summary>
    public MicroObject? Owner { get; internal set; }

    /// <summary>获取必备 Owner，未挂载时抛出。</summary>
    protected MicroObject RequireOwner()
        => Owner ?? throw new InvalidOperationException($"Component '{GetType().Name}' is not attached to a MicroObject.");

    /// <summary>获取宿主对象上的指定类型组件，不存在时返回 null。</summary>
    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent => Owner?.GetComponent<TComponent>();

    /// <summary>获取宿主对象上必备的指定类型组件，缺失则抛出。</summary>
    public TComponent GetRequiredComponent<TComponent>() where TComponent : MicroComponent
        => RequireOwner().GetComponent<TComponent>()
           ?? throw new InvalidOperationException($"Component '{typeof(TComponent).Name}' is required by '{GetType().Name}' but is not attached to the owner MicroObject.");

    /// <summary>在宿主对象上创建并追加指定类型组件（无参构造）。</summary>
    public ValueTask<TComponent> AddComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent, new()
        => RequireOwner().AddComponentAsync<TComponent>(cancellationToken);

    /// <summary>在宿主对象上追加一个已有组件实例。</summary>
    public ValueTask<TComponent> AddComponentAsync<TComponent>(TComponent component, CancellationToken cancellationToken = default) where TComponent : MicroComponent
        => RequireOwner().AddComponentAsync(component, cancellationToken);

    /// <summary>从宿主对象移除指定类型的组件。</summary>
    public ValueTask<bool> RemoveComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent
        => RequireOwner().RemoveComponentAsync<TComponent>(cancellationToken);

    /// <summary>销毁请求入口：挂在 obj 上时交给 obj 协调；脱离 obj 时直接本地实际拆毁。</summary>
    internal override ValueTask DestroyCoreAsync(CancellationToken cancellationToken = default)
        => Owner is { } owner ? owner.DestroyComponentAsync(this, cancellationToken) : DestroyFromOwnerAsync(cancellationToken);

    /// <summary>由所属 obj 协调完成的实际拆毁（走 base，不再经 Owner 路由，避免递归）。</summary>
    internal ValueTask DestroyFromOwnerAsync(CancellationToken cancellationToken = default)
        => base.DestroyCoreAsync(cancellationToken);
}
