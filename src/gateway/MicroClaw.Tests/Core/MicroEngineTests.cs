using FluentAssertions;
using MicroClaw.Core;
using MicroClaw.Extensions;
using MicroClaw.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

namespace MicroClaw.Tests.Core;

public sealed class MicroEngineTests
{
    [Fact]
    public async Task TickAsync_WhenStarted_OnlyInvokesUpdateServices()
    {
        var service = new TrackingService(order: 10);
        var updater = new TrackingUpdateService(order: 20);
        var engine = new MicroEngine(new NullServiceProvider(), [service, updater]);

        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(50));
        await engine.StopAsync();

        service.StartCount.Should().Be(1);
        service.StopCount.Should().Be(1);
        updater.StartCount.Should().Be(1);
        updater.StopCount.Should().Be(1);
        updater.TickCount.Should().Be(1);
        updater.LastDelta.Should().Be(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task RegisterObject_AfterStart_ActivatesAttachedComponentsImmediately()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();
        var component = host.AddComponent<TrackingComponent>();

        await engine.StartAsync();

        engine.RegisterObject(host).Should().BeTrue();

        component.State.Should().Be(MicroComponentState.Active);
        component.ActivatedCount.Should().Be(1);
        host.Engine.Should().BeSameAs(engine);
    }

    [Fact]
    public async Task StartAsync_WhenObjectActivationFails_RollsBackStartedState()
    {
        var service = new TrackingService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);
        var host = new MicroObject();
        var component = host.AddComponent<ThrowingComponent>();
        engine.RegisterObject(host).Should().BeTrue();

        Func<Task> act = () => engine.StartAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("activate failed");
        engine.State.Should().Be(MicroEngineState.Stopped);
        service.StartCount.Should().Be(1);
        service.StopCount.Should().Be(1);
        host.State.Should().Be(MicroObjectState.Created);
        component.State.Should().Be(MicroComponentState.Attached);
    }

    [Fact]
    public async Task StartAsync_WhenServiceStartFails_StopsHalfStartedService()
    {
        var service = new ThrowingStartService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        Func<Task> act = () => engine.StartAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("start failed");
        service.StopCount.Should().Be(1);
        service.State.Should().Be(MicroServiceState.Stopped);
        engine.State.Should().Be(MicroEngineState.Stopped);
    }

    [Fact]
    public void Dispose_WhenRegistered_RemovesObjectFromEngine()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();

        engine.RegisterObject(host).Should().BeTrue();

        host.Dispose();

        engine.Objects.Should().BeEmpty();
        host.Engine.Should().BeNull();
        host.State.Should().Be(MicroObjectState.Disposed);
    }

    [Fact]
    public async Task RegisterServiceAsync_WhenTickIsRunning_RejectsConcurrentMutation()
    {
        var updater = new BlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        await engine.StartAsync();

        Task tickTask = engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask();
        await updater.TickStarted;

        Func<Task> act = () => engine.RegisterServiceAsync(new TrackingService(order: 20)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*executing*");

        updater.ReleaseTick();
        await tickTask;
        await engine.StopAsync();
    }

    [Fact]
    public async Task HostedService_StopAsync_WhenCallerTokenIsCanceled_StillStopsEngine()
    {
        var updater = new CancellableUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await updater.TickStarted;

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await hostedService.StopAsync(cts.Token);

        engine.State.Should().Be(MicroEngineState.Stopped);
        updater.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_WhenEngineIsStarting_WaitsForStartupAndDoesNotBreakEngineState()
    {
        var service = new BlockingStartService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);
        var host = new MicroObject();

        engine.RegisterObject(host).Should().BeTrue();

        Task startTask = engine.StartAsync().AsTask();
        await service.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposeTask = Task.Run(host.Dispose);
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse();

        service.Release();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.State.Should().Be(MicroEngineState.Running);
        engine.Objects.Should().BeEmpty();
        host.State.Should().Be(MicroObjectState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenCancellationHappensDuringShutdown_UsesGraceTimeoutAndStillStopsEngine()
    {
        var updater = new DelayStopUpdateService(order: 10, stopDelay: TimeSpan.FromMilliseconds(200));
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);

        using CancellationTokenSource cts = new();
        Task stopTask = hostedService.StopAsync(cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.State.Should().Be(MicroEngineState.Stopped);
        updater.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenGateCannotBeAcquired_DoesNotChangeStateBeforeCleanupStarts()
    {
        var updater = new BlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        await engine.StartAsync();

        Task tickTask = engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask();
        await updater.TickStarted;

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        Func<Task> act = () => engine.StopAsync(cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        engine.State.Should().Be(MicroEngineState.Running);

        updater.ReleaseTick();
        await tickTask.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenWaitingForStopToFinish_SeesStableStoppedState()
    {
        var service = new BlockingStopService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        await engine.StartAsync();

        Task stopTask = engine.StopAsync().AsTask();
        await service.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task startTask = engine.StartAsync().AsTask();
        await Task.Delay(100);
        startTask.IsCompleted.Should().BeFalse();

        service.ReleaseStop();

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.State.Should().Be(MicroEngineState.Running);
        await engine.StopAsync();
    }

    [Fact]
    public async Task Dispose_WhenCalledInsideTick_ThrowsInsteadOfDeadlocking()
    {
        var host = new MicroObject();
        var updater = new ReentrantDisposeUpdateService(order: 10, host);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        engine.RegisterObject(host).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.DisposeException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("executing");
        host.State.Should().NotBe(MicroObjectState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task Dispose_WhenCalledFromDetachedThreadInsideTick_ThrowsInsteadOfBlocking()
    {
        var host = new MicroObject();
        var updater = new DetachedThreadDisposeUpdateService(order: 10, host);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        engine.RegisterObject(host).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.DisposeException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("executing");
        updater.ThreadCompleted.Should().BeTrue();
        host.State.Should().NotBe(MicroObjectState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task Dispose_WhenDeferredTaskRunsAfterTick_CompletesWithoutStaleReentryState()
    {
        var host = new MicroObject();
        var updater = new DeferredDisposeUpdateService(order: 10, host);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        engine.RegisterObject(host).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        await updater.ReleaseDisposeAsync();
        await updater.DisposeTask!.WaitAsync(TimeSpan.FromSeconds(5));

        updater.DisposeException.Should().BeNull();
        host.State.Should().Be(MicroObjectState.Disposed);
        engine.Objects.Should().BeEmpty();

        await engine.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenReenteredFromServiceStart_ThrowsInsteadOfDeadlocking()
    {
        var service = new ReentrantStartService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        await engine.StartAsync();

        service.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");

        await engine.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenReenteredFromServiceStop_ThrowsInsteadOfDeadlocking()
    {
        var service = new ReentrantStopService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        await engine.StartAsync();
        await engine.StopAsync();

        service.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");
    }

    [Fact]
    public async Task TickAsync_WhenReenteredFromTick_ThrowsInsteadOfDeadlocking()
    {
        var updater = new ReentrantTickUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");

        await engine.StopAsync();
    }

    [Fact]
    public void AddMicroEngine_RegistersEngineHostedServiceAndMicroServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMicroUpdateService<DiTrackingUpdateService>();
        services.AddMicroEngine();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        var engine = serviceProvider.GetRequiredService<MicroEngine>();
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();

        engine.Services.Should().ContainSingle().Which.Should().BeOfType<DiTrackingUpdateService>();
        hostedServices.Should().ContainSingle(service => service is MicroEngineHostedService);
    }

    [Fact]
    public async Task RegisterServiceAsync_WhenRollbackStopFails_ThrowsAggregateAndKeepsBrokenServiceAttached()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingStartAndStopService(order: 10);

        await engine.StartAsync();

        Func<Task> act = () => engine.RegisterServiceAsync(service).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().ContainSingle(ex => ex.Message == "start failed");
        exception.Which.InnerExceptions.Should().ContainSingle(ex => ex.Message == "stop failed");
        engine.State.Should().Be(MicroEngineState.Faulted);
        engine.Services.Should().Contain(service);
        service.Engine.Should().BeSameAs(engine);
        service.State.Should().Be(MicroServiceState.Starting);
    }

    [Fact]
    public void Constructor_WhenLaterServiceAttachFails_RollsBackEarlierServiceAttachments()
    {
        var firstService = new TrackingService(order: 10);
        var foreignService = new TrackingService(order: 20);
        var foreignEngine = new MicroEngine(new NullServiceProvider(), [foreignService]);

        Action act = () => _ = new MicroEngine(new NullServiceProvider(), [firstService, foreignService]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*one MicroEngine*");
        firstService.Engine.Should().BeNull();
        foreignService.Engine.Should().BeSameAs(foreignEngine);
    }

    [Fact]
    public async Task RegisterServiceAsync_WhenServiceBelongsToAnotherEngine_DoesNotStopForeignService()
    {
        var foreignService = new TrackingService(order: 10);
        var foreignEngine = new MicroEngine(new NullServiceProvider(), [foreignService]);
        var engine = new MicroEngine(new NullServiceProvider(), []);

        await foreignEngine.StartAsync();
        await engine.StartAsync();

        Func<Task> act = () => engine.RegisterServiceAsync(foreignService).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*one MicroEngine*");
        foreignService.StopCount.Should().Be(0);
        foreignService.Engine.Should().BeSameAs(foreignEngine);
        foreignEngine.State.Should().Be(MicroEngineState.Running);

        await foreignEngine.StopAsync();
        await engine.StopAsync();
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TrackingService(int order) : MicroService
    {
        public override int Order => order;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingUpdateService(int order) : MicroUpdateService
    {
        public override int Order => order;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int TickCount { get; private set; }

        public TimeSpan? LastDelta { get; private set; }

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;
            LastDelta = deltaTime;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingUpdateService(int order) : MicroUpdateService
    {
        private readonly TaskCompletionSource _tickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tickReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task TickStarted => _tickStarted.Task;

        public void ReleaseTick() => _tickReleased.TrySetResult();

        public override async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            _tickStarted.TrySetResult();
            await _tickReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CancellableUpdateService(int order) : MicroUpdateService
    {
        private readonly TaskCompletionSource _tickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public int StopCount { get; private set; }

        public Task TickStarted => _tickStarted.Task;

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public override async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            _tickStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class TrackingComponent : MicroComponent
    {
        public int ActivatedCount { get; private set; }

        protected override void OnActivated()
        {
            ActivatedCount++;
        }
    }

    private sealed class ThrowingComponent : MicroComponent
    {
        protected override void OnActivated()
        {
            throw new InvalidOperationException("activate failed");
        }
    }

    private sealed class ThrowingStartService(int order) : MicroService
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("start failed"));

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingStartAndStopService(int order) : MicroService
    {
        public override int Order => order;

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("start failed"));

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("stop failed"));
    }

    private sealed class BlockingStartService(int order) : MicroService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task Started => _started.Task;

        public void Release() => _released.TrySetResult();

        protected override async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DelayStopUpdateService(int order, TimeSpan stopDelay) : MicroUpdateService
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            await Task.Delay(stopDelay, cancellationToken);
        }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class ReentrantDisposeUpdateService(int order, MicroObject host) : MicroUpdateService
    {
        public override int Order => order;

        public Exception? DisposeException { get; private set; }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                host.Dispose();
            }
            catch (Exception ex)
            {
                DisposeException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DetachedThreadDisposeUpdateService(int order, MicroObject host) : MicroUpdateService
    {
        public override int Order => order;

        public Exception? DisposeException { get; private set; }

        public bool ThreadCompleted { get; private set; }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            Thread thread = new(() =>
            {
                try
                {
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    DisposeException = ex;
                }
                finally
                {
                    ThreadCompleted = true;
                }
            });

            thread.Start();
            thread.Join(TimeSpan.FromSeconds(2)).Should().BeTrue();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeferredDisposeUpdateService(int order, MicroObject host) : MicroUpdateService
    {
        private readonly TaskCompletionSource _releaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task? DisposeTask { get; private set; }

        public Exception? DisposeException { get; private set; }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            DisposeTask = Task.Run(async () =>
            {
                await _releaseDispose.Task.WaitAsync(cancellationToken);

                try
                {
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    DisposeException = ex;
                }
            }, cancellationToken);

            return ValueTask.CompletedTask;
        }

        public async Task ReleaseDisposeAsync()
        {
            _releaseDispose.TrySetResult();
            if (DisposeTask is not null)
                await DisposeTask;
        }
    }

    private sealed class ReentrantStartService(int order) : MicroService
    {
        public override int Order => order;

        public Exception? ReentryException { get; private set; }

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Engine!.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ReentryException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantStopService(int order) : MicroService
    {
        public override int Order => order;

        public Exception? ReentryException { get; private set; }

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Engine!.StopAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ReentryException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantTickUpdateService(int order) : MicroUpdateService
    {
        public override int Order => order;

        public Exception? ReentryException { get; private set; }

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                Engine!.TickAsync(deltaTime, cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ReentryException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingStopService(int order) : MicroService
    {
        private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public TaskCompletionSource StopStarted => _stopStarted;

        public void ReleaseStop() => _stopReleased.TrySetResult();

        protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            _stopStarted.TrySetResult();
            await _stopReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DiTrackingUpdateService : MicroUpdateService
    {
        public override int Order => 10;

        public override ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}