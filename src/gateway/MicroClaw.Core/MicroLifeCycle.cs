using MicroClaw.Core.Logging;

namespace MicroClaw.Core;

/// <summary>
/// 通用生命周期状态，由引擎/宿主驱动推进。
/// <para>
/// Created →(OnAwake)→ PendingStart →(OnStart)→ Active ⇄(OnEnable/OnDisable)⇄ Disabled
/// →(Destroy)→ PendingDestroy →(OnDestroy)→ Destroyed
/// </para>
/// </summary>
public enum MicroLifeCycleState
{
    /// <summary>已构造，尚未接入引擎/宿主。</summary>
    Created,

    /// <summary>已接入、OnAwake 完成，等待 OnStart —— “开始前”。</summary>
    PendingStart,

    /// <summary>OnStart 完成，正在被调度 —— “开始后”。</summary>
    Active,

    /// <summary>已停用（非终态，可经 OnEnable 回到 Active）。</summary>
    Disabled,

    /// <summary>已标记销毁，等待安全点执行 OnDestroy。</summary>
    PendingDestroy,

    /// <summary>OnDestroy 完成、已移除（终态）。</summary>
    Destroyed,
}

/// <summary>
/// 对象 / 组件 / 服务的统一生命周期基座。对象是被动的，只 override OnXxxAsync 钩子；
/// 由引擎/宿主调用内部驱动方法（Awake/Start/Enable/Disable/Destroy）推进 <see cref="LifeCycleState"/>。
/// 单个节点内部的状态推进经 <see cref="MicroClawLock"/>（可重入）串行化。
/// </summary>
public abstract class MicroLifecycle
{
    private readonly MicroClawLock _lock = new();
    private readonly MicroEvent _events = new();
    private volatile bool _isEnabled = true;
    private IMicroLogger? _logger;

    /// <summary>当前生命周期状态。</summary>
    public MicroLifeCycleState LifeCycleState { get; private set; } = MicroLifeCycleState.Created;

    /// <summary>是否启用；仅 Active 且 Enabled 才会被引擎调度。</summary>
    public bool Enabled { get => _isEnabled; private set => _isEnabled = value; }

    /// <summary>唯一标识，进程内自增唯一整数 id。</summary>
    public int InstanceId { get; } = Interlocked.Increment(ref _counter);

    /// <summary>
    /// 当前节点的 logger，分类名取自运行时类型。惰性初始化以便宿主在启动阶段替换
    /// <see cref="MicroLogger.Factory"/> 后仍能被后续实例拾取。
    /// </summary>
    protected IMicroLogger Logger => LazyInitializer.EnsureInitialized(ref _logger, CreateLogger);

    private IMicroLogger CreateLogger() => MicroLogger.Factory.CreateLogger(GetType());

    /// <summary>是否处于激活态。</summary>
    public bool IsActive => LifeCycleState == MicroLifeCycleState.Active;

    /// <summary>是否已销毁（终态）。</summary>
    public bool IsDestroyed => LifeCycleState == MicroLifeCycleState.Destroyed;

    /// <summary>订阅本节点上的某类事件。</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDestroyed();

        IDisposable subscription = _events.Subscribe(handler);

        if (IsDestroyed)
        {
            subscription.Dispose();
            ThrowIfDestroyed();
        }

        return subscription;
    }

    /// <summary>向本节点订阅者发布事件（按事件运行时类型分发）。</summary>
    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ThrowIfDestroyed();
        return _events.PublishAsync(domainEvent, cancellationToken);
    }

    /// <summary>设置启用状态：在 Active / Disabled 间切换并触发对应钩子。</summary>
    public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => enabled ? EnableCoreAsync(cancellationToken) : DisableCoreAsync(cancellationToken);

    // ---- 生命周期驱动（由引擎/宿主调用；均幂等，重复调用为 no-op） ----


    /// <summary>Created → PendingStart：执行 OnAwake（自我初始化）。异常只记录。</summary>
    internal virtual async ValueTask AwakeCoreAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDestroyed();
        if (LifeCycleState != MicroLifeCycleState.Created)
            return;

        await SafeInvokeAsync(OnAwakeAsync, "awake", cancellationToken);
        LifeCycleState = MicroLifeCycleState.PendingStart;
    }

    /// <summary>PendingStart → Active：执行 OnStart；若未启用则随即转 Disabled。异常只记录。</summary>
    internal virtual async ValueTask StartCoreAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDestroyed();
        if (LifeCycleState != MicroLifeCycleState.PendingStart)
            return;

        await SafeInvokeAsync(OnStartAsync, "start", cancellationToken);
        LifeCycleState = MicroLifeCycleState.Active;

        if (!Enabled)
            await DisableCoreAsync(cancellationToken);
    }

    /// <summary>Disabled → Active：执行 OnEnable。</summary>
    internal virtual async ValueTask EnableCoreAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDestroyed();
        Enabled = true;
        if (LifeCycleState != MicroLifeCycleState.Disabled)
            return;

        await SafeInvokeAsync(OnEnableAsync, "enable", cancellationToken);
        LifeCycleState = MicroLifeCycleState.Active;
    }

    /// <summary>Active → Disabled：执行 OnDisable。</summary>
    internal virtual async ValueTask DisableCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState != MicroLifeCycleState.Active)
            return;
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDestroyed();
        Enabled = false;
        await SafeInvokeAsync(OnDisableAsync, "disable", cancellationToken);
        LifeCycleState = MicroLifeCycleState.Disabled;
    }

    /// <summary>
    /// Core 内部销毁分发点。仅 Core 程序集内类型可重写，用于把对象/组件/服务的销毁交还给引擎协调。
    /// </summary>
    internal virtual async ValueTask DestroyCoreAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.EnterAsync(cancellationToken);
        if (LifeCycleState == MicroLifeCycleState.Destroyed)
            return;

        if (LifeCycleState == MicroLifeCycleState.Active)
            await SafeInvokeAsync(OnDisableAsync, "disable", cancellationToken);

        LifeCycleState = MicroLifeCycleState.PendingDestroy;
        await SafeInvokeAsync(OnDestroyAsync, "destroy", cancellationToken);

        _events.Clear();
        LifeCycleState = MicroLifeCycleState.Destroyed;
        Enabled = false;
    }

    // ---- 钩子（默认空实现，子类按需 override） ----

    /// <summary>上线时调用一次：只做“不依赖别人”的自我初始化。</summary>
    protected virtual ValueTask OnAwakeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>从 Disabled 重新启用时调用。</summary>
    protected virtual ValueTask OnEnableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>第一次 Tick 前调用一次：同一 obj 内可安全引用兄弟 component。</summary>
    protected virtual ValueTask OnStartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>被停用时调用。</summary>
    protected virtual ValueTask OnDisableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>移除前清理。</summary>
    protected virtual ValueTask OnDestroyAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>调用钩子并吞掉异常（只记录），保证状态机推进。</summary>
    private async ValueTask SafeInvokeAsync(Func<CancellationToken, ValueTask> hook, string phase, CancellationToken cancellationToken)
    {
        try
        {
            await hook(cancellationToken);
            WriteTrace($"{GetType().Name} {phase}.");
        }
        catch (Exception ex)
        {
            WriteTrace($"{GetType().Name} {phase} failed: {ex.Message}");
        }
    }

    /// <summary>确保当前节点尚未销毁。</summary>
    private void ThrowIfDestroyed()
    {
        if (LifeCycleState == MicroLifeCycleState.Destroyed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>写入生命周期级跟踪日志。</summary>
    protected void WriteTrace(string message) => Logger.LogDebug(message);

    /// <summary>根据收集到的异常数量统一抛出（单个保留堆栈，多个合并为 <see cref="AggregateException"/>）。</summary>
    protected static void ThrowIfNeeded(List<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    public override int GetHashCode()
    {
        return InstanceId;
    }

    private static int _counter = int.MinValue;
}
