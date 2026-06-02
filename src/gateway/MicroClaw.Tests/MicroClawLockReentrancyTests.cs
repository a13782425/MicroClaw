using MicroClaw.Core;

namespace MicroClaw.Tests;

public class MicroClawLockReentrancyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EnterAsync_NestedReentrantAcquire_DoesNotDeadlock()
    {
        var microClawLock = new MicroClawLock();

        using (await microClawLock.EnterAsync())
        {
            Assert.True(microClawLock.IsHeldByCurrentFlow);

            using (await microClawLock.EnterAsync().AsTask().WaitAsync(Timeout))
            {
                Assert.True(microClawLock.IsHeldByCurrentFlow);
            }

            Assert.True(microClawLock.IsHeldByCurrentFlow);
        }

        Assert.False(microClawLock.IsHeldByCurrentFlow);
    }

    [Fact]
    public async Task EnterAsync_DisposingNestedReentrantScope_DoesNotReleaseOuterScope()
    {
        var microClawLock = new MicroClawLock();
        using MicroClawLock.Releaser outer = await microClawLock.EnterAsync();
        using MicroClawLock.Releaser nested = await microClawLock.EnterAsync();
        TaskCompletionSource<bool> acquireAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        nested.Dispose();

        Task worker;
        using (ExecutionContext.SuppressFlow())
        {
            worker = Task.Run(async () =>
            {
                Task<MicroClawLock.Releaser> acquireTask = microClawLock.EnterAsync().AsTask();
                acquireAttempted.SetResult(acquireTask.IsCompleted);

                using (await acquireTask.WaitAsync(Timeout))
                {
                    entered.SetResult(true);
                }
            });
        }

        bool acquiredBeforeOuterRelease = await acquireAttempted.Task.WaitAsync(Timeout);

        Assert.False(acquiredBeforeOuterRelease);
        Assert.False(entered.Task.IsCompleted);

        outer.Dispose();

        await entered.Task.WaitAsync(Timeout);
        await worker.WaitAsync(Timeout);
    }

    [Fact]
    public async Task EnterAsync_ContendedAcquire_WaitsForCurrentHolderToRelease()
    {
        var microClawLock = new MicroClawLock();
        var owner = await microClawLock.EnterAsync();
        TaskCompletionSource<(bool AcquireCompleted, bool HeldBeforeAwait)> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task worker;
        using (ExecutionContext.SuppressFlow())
        {
            worker = Task.Run(async () =>
            {
                Task<MicroClawLock.Releaser> acquireTask = microClawLock.EnterAsync().AsTask();
                waiting.SetResult((acquireTask.IsCompleted, microClawLock.IsHeldByCurrentFlow));

                using (await acquireTask.WaitAsync(Timeout))
                {
                    entered.SetResult(true);
                }
            });
        }

        try
        {
            (bool acquireCompleted, bool heldBeforeAwait) = await waiting.Task.WaitAsync(Timeout);

            Assert.False(acquireCompleted);
            Assert.False(heldBeforeAwait);
            Assert.False(entered.Task.IsCompleted);
        }
        finally
        {
            owner.Dispose();
        }

        await entered.Task.WaitAsync(Timeout);
        await worker.WaitAsync(Timeout);
    }

    [Fact]
    public async Task EnterAsync_ConcurrentContention_IsMutuallyExclusive()
    {
        var microClawLock = new MicroClawLock();
        int inside = 0;
        int violations = 0;

        IEnumerable<Task> tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                using (await microClawLock.EnterAsync())
                {
                    int now = Interlocked.Increment(ref inside);
                    if (now != 1)
                        Interlocked.Increment(ref violations);

                    await Task.Yield();
                    Interlocked.Decrement(ref inside);
                }
            }
        }));

        await Task.WhenAll(tasks).WaitAsync(Timeout);

        Assert.Equal(0, violations);
        Assert.Equal(0, inside);
    }
}
