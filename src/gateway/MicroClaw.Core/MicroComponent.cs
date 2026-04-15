namespace MicroClaw.Core;

/// <summary>组件生命周期状态。</summary>
public enum MicroComponentState
{
    Detached,
    Attached,
    Initialized,
    Active,
}

/// <summary>
/// 组件抽象基类，挂载到 <see cref="MicroObject"/> 上提供特定功能。
/// 生命周期：Detached → Attached → Initialized → Active，销毁时逆序回退。
/// </summary>
public abstract class MicroComponent : MicroLifeCycle<MicroObject>
{
    /// <summary>当前组件状态。</summary>
    public MicroComponentState State => MapState(LifeCycleState);

    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent
        => Host?.GetComponent<TComponent>();

    public TComponent AddComponent<TComponent>(TComponent component) where TComponent : MicroComponent
        => GetRequiredHost().AddComponent(component);

    public TComponent AddComponent<TComponent>() where TComponent : MicroComponent, new()
        => GetRequiredHost().AddComponent<TComponent>();

    public bool RemoveComponent<TComponent>() where TComponent : MicroComponent
        => GetRequiredHost().RemoveComponent<TComponent>();

    public bool RemoveComponent(MicroComponent component)
        => GetRequiredHost().RemoveComponent(component);

    public override void Dispose()
    {
        Exception? removalException = null;
        bool stillAttachedToHost = false;

        if (Host is { } host)
        {
            try
            {
                host.RemoveComponent(this);
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
            base.Dispose();
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

    internal override ValueTask TickNodeAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        => TickCoreAsync(deltaTime, cancellationToken);

    internal void AttachTo(MicroObject host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Host is not null && !ReferenceEquals(Host, host))
            throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");

        AttachToHost(host);
    }

    internal void Initialize()
        => InitializeCore();

    internal void Activate()
        => ActivateCore();

    internal void Deactivate()
        => DeactivateCore();

    internal void Uninitialize()
        => UninitializeCore();

    internal void DetachFromHost()
        => DetachCore();

    internal void RollbackTo(MicroComponentState state)
        => RollbackToCore(MapState(state));

    private static MicroComponentState MapState(MicroLifeCycleState state)
        => state switch
        {
            MicroLifeCycleState.Detached => MicroComponentState.Detached,
            MicroLifeCycleState.Attached => MicroComponentState.Attached,
            MicroLifeCycleState.Initialized => MicroComponentState.Initialized,
            MicroLifeCycleState.Active => MicroComponentState.Active,
            MicroLifeCycleState.Disposed => MicroComponentState.Detached,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static MicroLifeCycleState MapState(MicroComponentState state)
        => state switch
        {
            MicroComponentState.Detached => MicroLifeCycleState.Detached,
            MicroComponentState.Attached => MicroLifeCycleState.Attached,
            MicroComponentState.Initialized => MicroLifeCycleState.Initialized,
            MicroComponentState.Active => MicroLifeCycleState.Active,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
}