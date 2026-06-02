namespace MicroClaw.Core;

/// <summary>
/// 跨 <c>await</c> 的可重入异步锁。
/// 同一逻辑执行流（async 调用链）内的嵌套加锁不会自死锁，
/// 不同执行流之间互斥。基于 <see cref="SemaphoreSlim"/> + <see cref="AsyncLocal{T}"/> 实现。
/// </summary>
/// <remarks>
/// 使用约束：嵌套加锁必须是线性的（顺序 await）。禁止在持锁期间于并行分支
/// 中再次进锁（例如 <c>Task.WhenAll(EnterAsync()..., EnterAsync()...)</c>），
/// 否则多个并行子流会同时命中重入快通路，破坏互斥并导致计数错乱。
/// </remarks>
public sealed class MicroClawLock : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<ScopeState?> _scope = new();

    /// <summary>
    /// 当前执行流是否已持有该锁。
    /// </summary>
    public bool IsHeldByCurrentFlow => _scope.Value is { IsActive: true };


    /// <summary>
    /// 获取锁。返回的 <see cref="Releaser"/> 必须用 <c>using</c> 释放。
    /// 若当前执行流已持锁，则仅增加重入计数、立即返回，不会再次等待信号量。
    /// </summary>
    public ValueTask<Releaser> EnterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScopeState? scope = _scope.Value;
        if (scope is { IsActive: true })
        {
            Interlocked.Increment(ref scope.Depth);
            return new ValueTask<Releaser>(new Releaser(this, scope));
        }

        ScopeState created = new() { Depth = 1 };
        _scope.Value = created;
        Task wait = _gate.WaitAsync(cancellationToken);
        if (wait.IsCompletedSuccessfully)
        {
            created.IsActive = true;
            return new ValueTask<Releaser>(new Releaser(this, created));
        }
        return WaitGateAsync(created, wait);
    }

    private async ValueTask<Releaser> WaitGateAsync(ScopeState scope, Task wait)
    {
        try
        {
            await wait.ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(_scope.Value, scope))
                _scope.Value = null;
            throw;
        }
        scope.IsActive = true;
        return new Releaser(this, scope);
    }
    private void Exit(ScopeState scope)
    {
        if (scope is not { IsActive: true } || scope.Depth <= 0)
            return;

        int depth = Interlocked.Decrement(ref scope.Depth);
        if (depth != 0)
            return;

        scope.IsActive = false;
        if (ReferenceEquals(_scope.Value, scope))
            _scope.Value = null;
        _gate.Release();
    }
    /// <summary>
    /// 销毁底层信号量。调用方必须保证此时没有任何执行流持有该锁。
    /// </summary>
    public void Dispose() => _gate.Dispose();

    /// <summary>锁的释放句柄；<see cref="Dispose"/> 时退出一层重入。</summary>
    public readonly struct Releaser : IDisposable
    {
        private readonly MicroClawLock _owner;

        private readonly ScopeState _scope;

        internal Releaser(MicroClawLock owner, ScopeState scope)
        {
            _owner = owner;
            _scope = scope;
        }
        public void Dispose() => _owner.Exit(_scope);
    }

    /// <summary>描述当前异步流持有的重入作用域。</summary>
    internal sealed class ScopeState
    {
        public int Depth;
        public bool IsActive = false;
    }
}