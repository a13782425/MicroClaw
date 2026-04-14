using FluentAssertions;
using MicroClaw.Core;
using MicroClaw.Services;
using Microsoft.Extensions.Logging.Abstractions;

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
}