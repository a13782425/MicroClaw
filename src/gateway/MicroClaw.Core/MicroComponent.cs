namespace MicroClaw.Core;

/// <summary>
/// 组件抽象基类，挂载到 <see cref="MicroObject"/> 上提供特定功能。
/// 生命周期：Detached → Attached → Initialized → Active，销毁时逆序回退。
/// </summary>
public abstract class MicroComponent : MicroLifeCycle<MicroObject>
{
    /// <summary>获取宿主对象上的指定类型组件。</summary>
    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent
        => Host?.GetComponent<TComponent>();

    /// <summary>向宿主对象追加一个已有组件实例。</summary>
    public ValueTask<TComponent> AddComponentAsync<TComponent>(TComponent component, CancellationToken cancellationToken = default) where TComponent : MicroComponent
        => GetRequiredHost().AddComponentAsync(component, cancellationToken);

    /// <summary>在宿主对象上创建并追加指定类型的组件。</summary>
    public ValueTask<TComponent> AddComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent, new()
        => GetRequiredHost().AddComponentAsync<TComponent>(cancellationToken);

    /// <summary>从宿主对象移除指定类型的组件。</summary>
    public ValueTask<bool> RemoveComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent
        => GetRequiredHost().RemoveComponentAsync<TComponent>(cancellationToken);

    /// <summary>从宿主对象移除指定的组件实例。</summary>
    public ValueTask<bool> RemoveComponentAsync(MicroComponent component, CancellationToken cancellationToken = default)
        => GetRequiredHost().RemoveComponentAsync(component, cancellationToken);

    /// <summary>先从宿主移除组件，再执行组件自身释放。</summary>
    public override async ValueTask DisposeAsync()
    {
        Exception? removalException = null;
        bool stillAttachedToHost = false;

        if (Host is { } host)
        {
            try
            {
                await host.RemoveComponentAsync(this);
            }
            catch (Exception ex)
            {
                removalException = ex;
                stillAttachedToHost = Host is not null;
            }
        }

        if (removalException is not null && stillAttachedToHost)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(removalException).Throw();

        Exception? disposeException = null;

        try
        {
            await base.DisposeAsync();
        }
        catch (Exception ex)
        {
            disposeException = ex;
        }

        if (removalException is not null && disposeException is not null)
            throw new AggregateException(removalException, disposeException);

        if (removalException is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(removalException).Throw();

        if (disposeException is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(disposeException).Throw();
    }

    /// <summary>将组件挂接到指定宿主对象。</summary>
    internal ValueTask AttachToAsync(MicroObject host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Host is not null && !ReferenceEquals(Host, host))
            throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");

        return AttachToHostAsync(host, cancellationToken);
    }

    /// <summary>推进组件到已初始化状态。</summary>
    internal ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        => InitializeCoreAsync(cancellationToken);

    /// <summary>推进组件到激活状态。</summary>
    internal ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        => ActivateCoreAsync(cancellationToken);

    /// <summary>将组件从激活状态回退到已初始化状态。</summary>
    internal ValueTask DeactivateAsync(CancellationToken cancellationToken = default)
        => DeactivateCoreAsync(cancellationToken);

    /// <summary>将组件从已初始化状态回退到已挂接状态。</summary>
    internal ValueTask UninitializeAsync(CancellationToken cancellationToken = default)
        => UninitializeCoreAsync(cancellationToken);

    /// <summary>将组件从当前宿主对象上分离。</summary>
    internal ValueTask DetachFromHostAsync(CancellationToken cancellationToken = default)
        => DetachCoreAsync(cancellationToken: cancellationToken);

    /// <summary>将组件回滚到指定的生命周期状态。</summary>
    internal ValueTask RollbackToAsync(MicroLifeCycleState state, CancellationToken cancellationToken = default)
        => RollbackToCoreAsync(state, cancellationToken);
}