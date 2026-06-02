using MicroClaw.Core.Logging;
using MicroClaw.Utils;

namespace MicroClaw.Core;

/// <summary>通用生命周期状态。</summary>
public enum MicroLifeCycleState
{
    Created,    // 已创建但未挂接
    Started,    // 构造完成、已绑定 host、已 Start，但未激活
    Active,     // 已激活
    Disable, // 已停用（非终态，仍可回退到 Active）
    Disposed,   // 已释放（终态）
}

/// <summary>为对象、组件和服务提供统一生命周期管理的抽象基类。</summary>
public abstract class MicroLifeCycle<THost> : IAsyncDisposable where THost : class
{
    private readonly MicroClawLock _lock = new();
    private bool _started = false;
    private readonly MicroEvent _events = new();
    private volatile bool _isEnabled = true;

    /// <summary>
    /// 是否启用；启用时 Start 后自动激活。
    /// </summary>
    public bool Enabled { get => _isEnabled; private set => _isEnabled = value; }

    private IMicroLogger? _logger;
    /// <summary>
    /// 当前生命周期节点的唯一标识，格式为不带连字符的 32 位小写字母数字字符串。用于跟踪和关联日志、事件等信息，
    /// </summary>
    public virtual string InstanceId { get; } = MicroClawUtils.GetUniqueId();
    /// <summary>
    /// 当前生命周期节点的 logger，分类名取自运行时类型。惰性初始化以便宿主在启动阶段
    /// 替换 <see cref="MicroLogger.Factory"/> 后仍能被后续实例拾取到。
    /// 使用 <see cref="LazyInitializer.EnsureInitialized{T}(ref T,Func{T})"/> 保证并发安全：
    /// 即使多个线程同时首次访问，最终也只有一个 <see cref="IMicroLogger"/> 实例胜出并被缓存。
    /// </summary>
    protected IMicroLogger Logger => LazyInitializer.EnsureInitialized(ref _logger, CreateLogger);

    private IMicroLogger CreateLogger() => MicroLogger.Factory.CreateLogger(GetType());

    /// <summary>当前关联的宿主对象。</summary>
    public THost? Host { get; private set; }

    /// <summary>当前内部生命周期状态。</summary>
    public MicroLifeCycleState LifeCycleState { get; private set; } = MicroLifeCycleState.Created;

    /// <summary>是否已经挂接到宿主。</summary>
    public bool IsAttached => Host is not null;

    /// <summary>是否已经进入激活状态。</summary>
    public bool IsActive => LifeCycleState == MicroLifeCycleState.Active;

    /// <summary>是否已经完成释放。</summary>
    public bool IsDisposed => LifeCycleState == MicroLifeCycleState.Disposed;

    /// <summary>Subscribes to an event type on this object only.</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();

        IDisposable subscription = _events.Subscribe(handler);

        if (IsDisposed)
        {
            subscription.Dispose();
            ThrowIfDisposed();
        }

        return subscription;
    }

    /// <summary>Publishes an event to subscribers registered for the event instance runtime type on this object only.</summary>
    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ThrowIfDisposed();
        return _events.PublishAsync(domainEvent, cancellationToken);
    }

    /// <summary>激活节点；若已激活或已释放则按规则处理。可从 Started / Disable 状态进入。</summary>
    public async ValueTask SetActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        if (active == Enabled)
            return;
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDisposed();
        Enabled = active;
        if (LifeCycleState == MicroLifeCycleState.Created)
            return;
        if (active)
            await ActiveCoreAsync(cancellationToken);
        else
            await DisableCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 将当前节点挂接到指定宿主：Start，并在启用时自动激活。异常只记录，不中断。
    /// </summary>
    public async ValueTask AttachToHostAsync(THost host, CancellationToken cancellationToken = default)
    {
        await AttachCoreAsync(host, cancellationToken);
    }

    /// <summary>
    /// 释放节点：若处于激活态先停用，再释放自身资源。每步异常只记录，保证终态必达。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync();
    }

    internal virtual async ValueTask AttachCoreAsync(THost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        using var _ = await _lock.EnterAsync(cancellationToken);
        ThrowIfDisposed();

        if (_started)
            return;

        if (Host is not null && !ReferenceEquals(Host, host))
            throw new InvalidOperationException("A lifecycle node can only belong to one host at a time.");

        Host = host;
        _started = true;

        try
        {
            await OnStartAsync(cancellationToken);
            LifeCycleState = MicroLifeCycleState.Started;
            WriteTrace($"{GetType().Name} started on {typeof(THost).Name}.");
        }
        catch (Exception ex)
        {
            WriteTrace($"{GetType().Name} start failed: {ex.Message}");
        }

        if (Enabled)
            await ActiveCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 激活核心逻辑（假定已持锁）。异常只记录，不回滚、不中断。
    /// </summary>
    internal virtual async ValueTask ActiveCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState == MicroLifeCycleState.Active)
            return;

        LifeCycleState = MicroLifeCycleState.Active;

        try
        {
            await OnActiveAsync(cancellationToken);
            WriteTrace($"{GetType().Name} activated.");
        }
        catch (Exception ex)
        {
            WriteTrace($"{GetType().Name} activation failed: {ex.Message}");
        }
    }
    /// <summary>
    /// 停用核心逻辑（假定已持锁）。异常只记录，不回滚、不中断。
    /// </summary>
    internal virtual async ValueTask DisableCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState == MicroLifeCycleState.Disable)
            return;

        LifeCycleState = MicroLifeCycleState.Disable;

        try
        {
            await OnDisableAsync(cancellationToken);
            WriteTrace($"{GetType().Name} disabled.");
        }
        catch (Exception ex)
        {
            WriteTrace($"{GetType().Name} disable failed: {ex.Message}");
        }
    }
    internal virtual async ValueTask DisposeCoreAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.EnterAsync();
        if (LifeCycleState == MicroLifeCycleState.Disposed)
            return;

        if (LifeCycleState == MicroLifeCycleState.Active)
            await DisableCoreAsync();

        try
        {
            await OnDisposedAsync();
            WriteTrace($"{GetType().Name} disposed.");
        }
        catch (Exception ex)
        {
            WriteTrace($"{GetType().Name} dispose failed: {ex.Message}");
        }
        finally
        {
            _events.Clear();
        }

        LifeCycleState = MicroLifeCycleState.Disposed;
        Enabled = false;
    }

    /// <summary>启动阶段的扩展钩子，在节点创建后自动调用一次。</summary>
    protected virtual ValueTask OnStartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>激活阶段的扩展钩子，与 <see cref="OnDisableAsync"/> 相对。</summary>
    protected virtual ValueTask OnActiveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>停用阶段的扩展钩子，与 <see cref="OnActiveAsync"/> 相对。</summary>
    protected virtual ValueTask OnDisableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>释放阶段的扩展钩子。</summary>
    protected virtual ValueTask OnDisposedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>确保当前节点尚未被释放。</summary>
    private void ThrowIfDisposed()
    {
        if (LifeCycleState == MicroLifeCycleState.Disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
    /// <summary>
    /// 写入生命周期级跟踪日志。
    /// </summary>
    protected void WriteTrace(string message)
    {
        Logger.LogDebug(message);
    }
}