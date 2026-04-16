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
    private static void Await(ValueTask task) => task.AsTask().GetAwaiter().GetResult();

    private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

    private static int GetSchedulerRegistrationCount(MicroEngine engine)
    {
        object scheduler = typeof(MicroEngine)
            .GetField("_tickScheduler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(engine)!;

        var registrations = (System.Collections.IDictionary)scheduler
            .GetType()
            .GetField("_registrations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(scheduler)!;

        return registrations.Count;
    }

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
    public async Task TickAsync_WhenStarted_InvokesActiveObjectsThatImplementTickable()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new TickingObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(25));
        await engine.StopAsync();

        host.TickCount.Should().Be(1);
        host.LastDelta.Should().Be(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task RegisterObject_AfterStart_ActivatesAttachedComponentsImmediately()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());

        await engine.StartAsync();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        component.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        component.ActivatedCount.Should().Be(1);
        host.Engine.Should().BeSameAs(engine);
    }

    [Fact]
    public async Task TickableObject_WhenManuallyDeactivatedAndReactivated_SyncsSchedulerRegistration()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new TickingObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        host.TickCount.Should().Be(1);

        await host.DeactivateAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        host.TickCount.Should().Be(1);

        await host.ActivateAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        host.TickCount.Should().Be(2);

        await engine.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenObjectActivationFails_RollsBackStartedState()
    {
        var service = new TrackingService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<ThrowingComponent>());
        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Func<Task> act = () => engine.StartAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("activate failed");
        engine.State.Should().Be(MicroEngineState.Stopped);
        service.StartCount.Should().Be(1);
        service.StopCount.Should().Be(1);
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);
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
    public async Task StartAsync_WhenServiceInitializationFails_DoesNotInvokeStopCallback()
    {
        var service = new ThrowingInitializeService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        Func<Task> act = () => engine.StartAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("initialize failed");
        service.StopCount.Should().Be(0);
        service.State.Should().Be(MicroServiceState.Stopped);
        engine.State.Should().Be(MicroEngineState.Stopped);
    }

    [Fact]
    public async Task StartAsync_WhenActivationHookFails_RunsDeactivationHookRollback()
    {
        var service = new ThrowingActivationHookService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        Func<Task> act = () => engine.StartAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("activate failed");
        service.DeactivatedCount.Should().Be(1);
        service.State.Should().Be(MicroServiceState.Stopped);
        engine.State.Should().Be(MicroEngineState.Stopped);
    }

    [Fact]
    public void Dispose_WhenRegistered_RemovesObjectFromEngine()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Await(host.DisposeAsync());

        engine.Objects.Should().BeEmpty();
        host.Engine.Should().BeNull();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_WhenRegisteredObject_RemovesItFromEngine()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        await host.DisposeAsync();

        engine.Objects.Should().BeEmpty();
        host.Engine.Should().BeNull();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_WhenTickableObjectDisposeFails_RestoresTickRegistration()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new TickingObject();
        var component = new BlockingAttachComponent();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        Task addTask = Task.Run(() => Await(host.AddComponentAsync(component)));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Func<Task> act = () => host.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*transition*");
        host.Engine.Should().BeSameAs(engine);
        engine.Objects.Should().Contain(host);

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        host.TickCount.Should().Be(2);

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));

        await host.DisposeAsync();
        engine.Objects.Should().NotContain(host);
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
    public async Task TickAsync_WhenTickableThrows_PropagatesFailureAndKeepsEngineRunning()
    {
        var failing = new ThrowingTickUpdateService(order: 10);
        var observer = new TrackingUpdateService(order: 20);
        var engine = new MicroEngine(new NullServiceProvider(), [failing, observer]);

        await engine.StartAsync();

        Func<Task> act = () => engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tick failed");
        engine.State.Should().Be(MicroEngineState.Running);
        observer.TickCount.Should().Be(1);

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        observer.TickCount.Should().Be(2);

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
    public async Task UnregisterServiceAsync_WhenBackgroundTickIsRunning_DrainsBeforeDetaching()
    {
        var updater = new BlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await updater.TickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Task unregisterTask = engine.UnregisterServiceAsync(updater).AsTask();
        await Task.Delay(100);

        unregisterTask.IsCompleted.Should().BeFalse();

        updater.ReleaseTick();
        await unregisterTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.Services.Should().NotContain(updater);
        updater.Engine.Should().BeNull();
        updater.State.Should().Be(MicroServiceState.Stopped);

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnregisterServiceAsync_WhenDrainWaitIsCanceled_LeavesServiceSchedulable()
    {
        var updater = new RearmableBlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await updater.FirstTickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        Func<Task> act = () => engine.UnregisterServiceAsync(updater, cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();

        updater.ReleaseFirstTick();
        await updater.SecondTickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        engine.Services.Should().Contain(updater);

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispose_WhenEngineIsStarting_WaitsForStartupAndDoesNotBreakEngineState()
    {
        var service = new BlockingStartService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);
        var host = new MicroObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Task startTask = engine.StartAsync().AsTask();
        await service.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposeTask = Task.Run(() => Await(host.DisposeAsync()));
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse();

        service.Release();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.State.Should().Be(MicroEngineState.Running);
        engine.Objects.Should().BeEmpty();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task DisposeService_WhenEngineIsStarting_WaitsForStartupAndDoesNotBreakEngineState()
    {
        var service = new BlockingStartService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [service]);

        Task startTask = engine.StartAsync().AsTask();
        await service.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposeTask = Task.Run(() => Await(service.DisposeAsync()));
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse();

        service.Release();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        engine.State.Should().Be(MicroEngineState.Running);
        engine.Services.Should().NotContain(service);
        service.Engine.Should().BeNull();
        service.IsDisposed.Should().BeTrue();
        service.State.Should().Be(MicroServiceState.Stopped);
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
    public async Task StopAsync_WhenTickDrainTimesOut_CanRetryAfterTickCompletes()
    {
        var updater = new NonCancelableBlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        using CancellationTokenSource runLoopCts = new();

        await engine.StartAsync();
        Task runLoopTask = engine.RunAsync(runLoopCts.Token);
        await updater.TickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await runLoopCts.CancelAsync();

        using CancellationTokenSource stopCts = new(TimeSpan.FromMilliseconds(100));
        Func<Task> firstStop = () => engine.StopAsync(stopCts.Token).AsTask();

        await firstStop.Should().ThrowAsync<OperationCanceledException>();
        engine.State.Should().Be(MicroEngineState.Faulted);
        updater.State.Should().Be(MicroServiceState.Running);

        updater.ReleaseTick();
        await runLoopTask.WaitAsync(TimeSpan.FromSeconds(5));

        await engine.StopAsync();
        engine.State.Should().Be(MicroEngineState.Stopped);
    }

    [Fact]
    public async Task StopAsync_WhenRunLoopTokenIsNotCanceled_StopsBackgroundLoop()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        using CancellationTokenSource runLoopCts = new();
        using CancellationTokenSource stopCts = new(TimeSpan.FromSeconds(5));

        await engine.StartAsync();
        Task runLoopTask = engine.RunAsync(runLoopCts.Token);

        try
        {
            await Task.Delay(150);
            await engine.StopAsync(stopCts.Token);
            await runLoopTask.WaitAsync(TimeSpan.FromSeconds(5));

            engine.State.Should().Be(MicroEngineState.Stopped);
        }
        finally
        {
            if (!runLoopTask.IsCompleted)
                await runLoopCts.CancelAsync();
        }
    }

    [Fact]
    public async Task TickAsync_WhenBackgroundRunLoopIsActive_ThrowsInsteadOfMixingFrames()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        using CancellationTokenSource runLoopCts = new();

        await engine.StartAsync();
        Task runLoopTask = engine.RunAsync(runLoopCts.Token);

        try
        {
            Func<Task> act = () => engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*background tick loop is running*");
        }
        finally
        {
            await runLoopCts.CancelAsync();
            await runLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task RunAsync_WhenTickableThrows_IsolatesFailureAndKeepsLoopRunning()
    {
        var failing = new ThrowingTickUpdateService(order: 10);
        var observer = new ThrowingStopUpdateService(order: 20)
        {
            FailOnStop = false,
        };
        var engine = new MicroEngine(new NullServiceProvider(), [failing, observer]);

        await engine.StartAsync();
        Task runLoopTask = engine.RunAsync(CancellationToken.None);

        await observer.SecondTickStarted.WaitAsync(TimeSpan.FromSeconds(5));
        engine.State.Should().Be(MicroEngineState.Running);
        runLoopTask.IsCompleted.Should().BeFalse();

        await engine.StopAsync();
        engine.State.Should().Be(MicroEngineState.Stopped);
        await runLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
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

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.DisposeException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("executing");
        host.LifeCycleState.Should().NotBe(MicroLifeCycleState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task Dispose_WhenCalledFromDetachedThreadInsideTick_ThrowsInsteadOfBlocking()
    {
        var host = new MicroObject();
        var updater = new DetachedThreadDisposeUpdateService(order: 10, host);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.DisposeException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("executing");
        updater.ThreadCompleted.Should().BeTrue();
        host.LifeCycleState.Should().NotBe(MicroLifeCycleState.Disposed);

        await engine.StopAsync();
    }

    [Fact]
    public async Task Dispose_WhenDeferredTaskRunsAfterTick_CompletesWithoutStaleReentryState()
    {
        var host = new MicroObject();
        var updater = new DeferredDisposeUpdateService(order: 10, host);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        await updater.ReleaseDisposeAsync();
        await updater.DisposeTask!.WaitAsync(TimeSpan.FromSeconds(5));

        updater.DisposeException.Should().BeNull();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        engine.Objects.Should().BeEmpty();

        await engine.StopAsync();
    }

    [Fact]
    public async Task UnregisterObject_WhenDeactivateFails_RestoresTickRegistration()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new TickableObjectWithFailingDeactivate();
        var component = Await(host.AddComponentAsync<DeactivateFailingComponent>());

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        Action act = () => Await(engine.UnregisterObjectAsync(host));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("deactivate failed");
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        engine.Objects.Should().Contain(host);

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        host.TickCount.Should().Be(2);

        component.FailOnDeactivate = false;
        Await(host.RemoveComponentAsync<DeactivateFailingComponent>()).Should().BeTrue();
        Await(engine.UnregisterObjectAsync(host)).Should().BeTrue();
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
    public async Task TickAsync_WhenMultipleTickablesReenter_DoesNotCorruptExecutionScope()
    {
        var first = new ReentrantTickUpdateService(order: 10);
        var second = new ReentrantTickUpdateService(order: 20);
        var engine = new MicroEngine(new NullServiceProvider(), [first, second]);

        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        first.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");
        second.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        await engine.StopAsync();
    }

    [Fact]
    public async Task TickAsync_WhenCanceledWhileTickStillRunning_KeepsExecutionGateHeldUntilCompletion()
    {
        var updater = new NonCancelableBlockingUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        using CancellationTokenSource tickCts = new();

        await engine.StartAsync();
        Task tickTask = engine.TickAsync(TimeSpan.FromMilliseconds(10), tickCts.Token).AsTask();
        await updater.TickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await tickCts.CancelAsync();

        Func<Task> act = () => engine.RegisterServiceAsync(new TrackingService(order: 20)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*executing*");

        updater.ReleaseTick();

        Func<Task> waitForTick = () => tickTask;

        await waitForTick.Should().ThrowAsync<OperationCanceledException>();
        await engine.StopAsync();
    }

    [Fact]
    public async Task TickAsync_WhenObjectDeactivatesItself_ThrowsInsteadOfDeadlocking()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new SelfDeactivatingTickingObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        host.DeactivationException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("tick execution");
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Active);

        await engine.StopAsync();
    }

    [Fact]
    public async Task RunAsync_WhenReenteredFromTick_ThrowsInsteadOfDeadlocking()
    {
        var updater = new ReentrantTickUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await updater.TickAttempted.WaitAsync(TimeSpan.FromSeconds(5));

        updater.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenRunLoopIsReenteredFromTick_ThrowsInsteadOfDeadlocking()
    {
        var updater = new ReentrantRunLoopUpdateService(order: 10);
        var engine = new MicroEngine(new NullServiceProvider(), [updater]);

        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        updater.ReentryException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("re-entered");

        await engine.StopAsync();
    }

    [Fact]
    public async Task AddMicroEngine_RegistersEngineHostedServiceAndMicroServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMicroService<DiTrackingUpdateService>();
        services.AddMicroEngine();

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

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
        service.State.Should().Be(MicroServiceState.Stopping);
    }

    [Fact]
    public async Task Dispose_WhenRegisteredService_RemovesItFromEngineAndMarksDisposed()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new TrackingService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        await service.DisposeAsync();

        engine.Services.Should().NotContain(service);
        service.Engine.Should().BeNull();
        service.IsDisposed.Should().BeTrue();
        service.State.Should().Be(MicroServiceState.Stopped);
        service.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenRegisteredService_RemovesItFromEngineAndMarksDisposed()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new TrackingService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        await service.DisposeAsync();

        engine.Services.Should().NotContain(service);
        service.Engine.Should().BeNull();
        service.IsDisposed.Should().BeTrue();
        service.State.Should().Be(MicroServiceState.Stopped);
        service.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_WhenServiceStopFails_DoesNotInvokeStopTwice()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingStopService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        Func<Task> act = () => service.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("stop failed");
        service.StopCount.Should().Be(1);
        service.IsDisposed.Should().BeTrue();
        engine.Services.Should().NotContain(service);
    }

    [Fact]
    public async Task Dispose_WhenUninitializeFails_RetriesUninitializeWithoutRepeatingStop()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingUninitializeService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        Func<Task> act = () => service.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("uninitialize failed");
        service.StopCount.Should().Be(1);
        service.UninitializeCount.Should().Be(2);
        service.IsDisposed.Should().BeTrue();
        engine.Services.Should().NotContain(service);
    }

    [Fact]
    public async Task UnregisterServiceAsync_WhenUninitializeFails_LeavesServiceNotStarted()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingAlwaysUninitializeService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        Func<Task> act = () => engine.UnregisterServiceAsync(service).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("uninitialize failed");
        service.State.Should().Be(MicroServiceState.Stopping);
        service.IsStarted.Should().BeFalse();
        service.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);
        engine.Services.Should().Contain(service);
    }

    [Fact]
    public async Task UnregisterServiceAsync_WhenStopFails_RestoresTickRegistration()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingStopUpdateService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();
        await engine.TickAsync(TimeSpan.FromMilliseconds(10));

        Func<Task> act = () => engine.UnregisterServiceAsync(service).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("stop failed");
        service.State.Should().Be(MicroServiceState.Running);
        engine.Services.Should().Contain(service);

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        service.TickCount.Should().Be(2);

        service.FailOnStop = false;
        await service.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WhenServiceStopFails_DoesNotLeaveSchedulerRegistrations()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingStopUpdateService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        GetSchedulerRegistrationCount(engine).Should().Be(1);

        Func<Task> act = () => engine.StopAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("stop failed");
        engine.State.Should().Be(MicroEngineState.Faulted);
        service.State.Should().Be(MicroServiceState.Running);
        GetSchedulerRegistrationCount(engine).Should().Be(0);

        service.FailOnStop = false;
        await service.DisposeAsync();
    }

    [Fact]
    public async Task UnregisterServiceAsync_WhenTickFailureAlreadyIsolated_DoesNotRescheduleService()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingTickThenStopUpdateService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        Func<Task> firstTick = () => engine.TickAsync(TimeSpan.FromMilliseconds(10)).AsTask();

        await firstTick.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tick failed");
        service.TickCount.Should().Be(1);

        Func<Task> act = () => engine.UnregisterServiceAsync(service).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("stop failed");
        engine.Services.Should().Contain(service);

        await engine.TickAsync(TimeSpan.FromMilliseconds(10));
        service.TickCount.Should().Be(1);

        service.FailOnStop = false;
        await service.DisposeAsync();
    }

    [Fact]
    public async Task UnregisterServiceAsync_WhenBackgroundRunStopFails_RestoresTickRegistration()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingStopUpdateService(order: 10);
        var hostedService = new MicroEngineHostedService(engine, NullLogger<MicroEngineHostedService>.Instance);

        await engine.RegisterServiceAsync(service);
        await hostedService.StartAsync(CancellationToken.None);
        await service.FirstTickStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Func<Task> act = () => engine.UnregisterServiceAsync(service).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("stop failed");
        service.State.Should().Be(MicroServiceState.Running);
        engine.Services.Should().Contain(service);

        await service.SecondTickStarted.WaitAsync(TimeSpan.FromSeconds(5));
        service.TickCount.Should().BeGreaterThanOrEqualTo(2);

        service.FailOnStop = false;
        await service.DisposeAsync();
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhenServiceIsPartiallyStopped_RetriesCleanupAndAllowsRestart()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new ThrowingUninitializeService(order: 10);

        await engine.RegisterServiceAsync(service);
        await engine.StartAsync();

        Func<Task> firstStop = () => engine.StopAsync().AsTask();

        await firstStop.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("uninitialize failed");
        engine.State.Should().Be(MicroEngineState.Faulted);
        service.State.Should().Be(MicroServiceState.Stopping);
        service.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);
        service.StopCount.Should().Be(1);

        await engine.StopAsync();

        engine.State.Should().Be(MicroEngineState.Stopped);
        service.State.Should().Be(MicroServiceState.Stopped);
        service.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);
        service.StopCount.Should().Be(1);

        await engine.StartAsync();
        engine.State.Should().Be(MicroEngineState.Running);

        await engine.StopAsync();
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

    private sealed class TrackingUpdateService(int order) : MicroService, IMicroTickable
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

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;
            LastDelta = deltaTime;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingUpdateService(int order) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _tickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tickReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task TickStarted => _tickStarted.Task;

        public void ReleaseTick() => _tickReleased.TrySetResult();

        public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            _tickStarted.TrySetResult();
            await _tickReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingTickUpdateService(int order) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("tick failed"));
    }

    private sealed class RearmableBlockingUpdateService(int order) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _firstTickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstTickReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondTickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tickCount;

        public override int Order => order;

        public Task FirstTickStarted => _firstTickStarted.Task;

        public Task SecondTickStarted => _secondTickStarted.Task;

        public void ReleaseFirstTick() => _firstTickReleased.TrySetResult();

        public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            int tickCount = Interlocked.Increment(ref _tickCount);

            if (tickCount == 1)
            {
                _firstTickStarted.TrySetResult();
                await _firstTickReleased.Task.WaitAsync(cancellationToken);
                return;
            }

            if (tickCount == 2)
                _secondTickStarted.TrySetResult();
        }
    }

    private sealed class CancellableUpdateService(int order) : MicroService, IMicroTickable
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

        public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            _tickStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class NonCancelableBlockingUpdateService(int order) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _tickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tickReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task TickStarted => _tickStarted.Task;

        public void ReleaseTick() => _tickReleased.TrySetResult();

        public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            _tickStarted.TrySetResult();
            await _tickReleased.Task;
        }
    }

    private sealed class BlockingAttachComponent : MicroComponent
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public TaskCompletionSource AttachedStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AllowAttach() => _gate.Set();

        protected override ValueTask OnAttachedAsync(CancellationToken cancellationToken = default)
        {
            AttachedStarted.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Attach gate timed out.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingComponent : MicroComponent
    {
        public int ActivatedCount { get; private set; }

        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            ActivatedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TickingObject : MicroObject, IMicroTickable
    {
        public int TickCount { get; private set; }

        public TimeSpan? LastDelta { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;
            LastDelta = deltaTime;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TickableObjectWithFailingDeactivate : MicroObject, IMicroTickable
    {
        public int TickCount { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SelfDeactivatingTickingObject : MicroObject, IMicroTickable
    {
        public Exception? DeactivationException { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                DeactivateAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DeactivationException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeactivateFailingComponent : MicroComponent
    {
        public bool FailOnDeactivate { get; set; } = true;

        protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
            => FailOnDeactivate
                ? ValueTask.FromException(new InvalidOperationException("deactivate failed"))
                : ValueTask.CompletedTask;
    }

    private sealed class ThrowingComponent : MicroComponent
    {
        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("activate failed"));
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

    private sealed class ThrowingInitializeService(int order) : MicroService
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("initialize failed"));

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingActivationHookService(int order) : MicroService
    {
        public override int Order => order;

        public int DeactivatedCount { get; private set; }

        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("activate failed"));

        protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
        {
            DeactivatedCount++;
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

    private sealed class ThrowingStopService(int order) : MicroService
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        protected override ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.FromException(new InvalidOperationException("stop failed"));
        }
    }

    private sealed class ThrowingStopUpdateService(int order) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _firstTickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondTickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public bool FailOnStop { get; set; } = true;

        public int TickCount { get; private set; }

        public Task FirstTickStarted => _firstTickStarted.Task;

        public Task SecondTickStarted => _secondTickStarted.Task;

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
            => FailOnStop
                ? ValueTask.FromException(new InvalidOperationException("stop failed"))
                : ValueTask.CompletedTask;

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;

            if (TickCount == 1)
                _firstTickStarted.TrySetResult();

            if (TickCount == 2)
                _secondTickStarted.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingTickThenStopUpdateService(int order) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public bool FailOnStop { get; set; } = true;

        public int TickCount { get; private set; }

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
            => FailOnStop
                ? ValueTask.FromException(new InvalidOperationException("stop failed"))
                : ValueTask.CompletedTask;

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            TickCount++;

            if (TickCount == 1)
                return ValueTask.FromException(new InvalidOperationException("tick failed"));

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingUninitializeService(int order) : MicroService
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        public int UninitializeCount { get; private set; }

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
        {
            UninitializeCount++;

            if (UninitializeCount == 1)
                return ValueTask.FromException(new InvalidOperationException("uninitialize failed"));

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAlwaysUninitializeService(int order) : MicroService
    {
        public override int Order => order;

        protected override ValueTask StopAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        protected override ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("uninitialize failed"));
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

    private sealed class DelayStopUpdateService(int order, TimeSpan stopDelay) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public int StopCount { get; private set; }

        protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            await Task.Delay(stopDelay, cancellationToken);
        }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class ReentrantDisposeUpdateService(int order, MicroObject host) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public Exception? DisposeException { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DisposeException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DetachedThreadDisposeUpdateService(int order, MicroObject host) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public Exception? DisposeException { get; private set; }

        public bool ThreadCompleted { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            Thread thread = new(() =>
            {
                try
                {
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    private sealed class DeferredDisposeUpdateService(int order, MicroObject host) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _releaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Task? DisposeTask { get; private set; }

        public Exception? DisposeException { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            DisposeTask = Task.Run(async () =>
            {
                await _releaseDispose.Task.WaitAsync(cancellationToken);

                try
                {
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    private sealed class ReentrantTickUpdateService(int order) : MicroService, IMicroTickable
    {
        private readonly TaskCompletionSource _tickAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Order => order;

        public Exception? ReentryException { get; private set; }

        public Task TickAttempted => _tickAttempted.Task;

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                _tickAttempted.TrySetResult();
                Engine!.TickAsync(deltaTime, cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ReentryException = ex;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantRunLoopUpdateService(int order) : MicroService, IMicroTickable
    {
        public override int Order => order;

        public Exception? ReentryException { get; private set; }

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            try
            {
                Engine!.RunAsync(cancellationToken).GetAwaiter().GetResult();
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

    private sealed class DiTrackingUpdateService : MicroService, IMicroTickable
    {
        public override int Order => 10;

        public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}