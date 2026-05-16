using FluentAssertions;
using MicroClaw.Core;
using System.Collections.Concurrent;

namespace MicroClaw.Tests.Core;

public sealed class MicroObjectTests
{
    private static void Await(ValueTask task) => task.AsTask().GetAwaiter().GetResult();

    private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();


    [Fact]
    public void AddComponent_ComponentAlreadyAttachedToAnotherHost_Throws()
    {
        var firstHost = new MicroObject();
        var secondHost = new MicroObject();
        var component = new TrackingComponent();

        Await(firstHost.AddComponentAsync(component));

        Action act = () => Await(secondHost.AddComponentAsync(component));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*one MicroObject*");
    }

    [Fact]
    public void GetAddRemoveComponent_SameTypeRoundTrip_Works()
    {
        var host = new MicroObject();

        var component = Await(host.AddComponentAsync<TrackingComponent>());

        host.GetComponent<TrackingComponent>().Should().BeSameAs(component);
        component.GetComponent<TrackingComponent>().Should().BeSameAs(component);

        Await(host.RemoveComponentAsync<TrackingComponent>()).Should().BeTrue();
        host.GetComponent<TrackingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Detached);
        component.DetachedCount.Should().Be(1);
    }

    [Fact]
    public void GetRequiredComponent_WhenSiblingExists_ReturnsComponent()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());
        var sibling = Await(host.AddComponentAsync<RequiredComponent>());

        sibling.GetRequiredComponent<TrackingComponent>().Should().BeSameAs(component);
    }

    [Fact]
    public void GetRequiredComponent_WhenSiblingIsMissing_ThrowsInvalidOperationException()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());

        Action act = () => component.GetRequiredComponent<RequiredComponent>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequiredComponent*");
    }

    [Fact]
    public async Task MicroComponentEventHelpers_SubscribeAndPublishThroughHostEvents()
    {
        var host = new MicroObject();
        var publisher = await host.AddComponentAsync<TrackingComponent>();
        var subscriber = await host.AddComponentAsync<RequiredComponent>();
        var observed = new List<string>();

        IDisposable subscription = subscriber.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await publisher.PublishAsync(new TestDomainEvent("before"));
        subscription.Dispose();
        await publisher.PublishAsync(new TestDomainEvent("after"));

        observed.Should().Equal("before");
    }

    [Fact]
    public void AddComponent_HostAlreadyActive_InitializesAndActivatesComponent()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        Await(host.ActivateAsync());

        var component = Await(host.AddComponentAsync<TrackingComponent>());

        component.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        component.InitializedCount.Should().Be(1);
        component.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public async Task AddComponentAsync_HostAlreadyActive_InitializesAndActivatesComponent()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        await host.ActivateAsync();

        var component = await host.AddComponentAsync<TrackingComponent>();

        component.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        component.InitializedCount.Should().Be(1);
        component.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public void AddComponent_WhenActivationFails_RollsBackAttachment()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        Await(host.ActivateAsync());
        var component = new ThrowingComponent();

        Action act = () => Await(host.AddComponentAsync(component));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("activate failed");
        host.GetComponent<ThrowingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Detached);
    }

    [Fact]
    public async Task AddComponent_WhenAnotherThreadRemovesDuringAttach_RejectsConcurrentMutation()
    {
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        Task addTask = Task.Run(() => Await(host.AddComponentAsync(component)));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => Await(host.RemoveComponentAsync(component));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lifecycle transition*");

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingAttachComponent>().Should().BeSameAs(component);
        component.Host.Should().BeSameAs(host);
    }

    [Fact]
    public async Task AddComponentAsync_WhenAttachIsInProgress_ComponentIsNotVisibleUntilCompleted()
    {
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        Task addTask = Task.Run(() => Await(host.AddComponentAsync(component)));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingAttachComponent>().Should().BeNull();
        host.Components.Should().BeEmpty();

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingAttachComponent>().Should().BeSameAs(component);
        host.Components.Should().ContainSingle().Which.Should().BeSameAs(component);
    }

    [Fact]
    public void Activate_WhenComponentMutatesHost_ReentrantMutationIsRejected()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        Await(host.AddComponentAsync<ReentrantComponent>());

        Action act = () => Await(host.ActivateAsync());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lifecycle transition*");
        host.GetComponent<TrackingComponent>().Should().BeNull();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);
    }

    [Fact]
    public async Task ActivateAsync_WhenRegisteredObjectIsActivating_RejectsConcurrentUnregister()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new BlockingActivateObject();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Task activateTask = Task.Run(() => Await(host.ActivateAsync()));
        await host.ActivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Func<Task> act = () => engine.UnregisterObjectAsync(host).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*executing*");

        host.AllowActivation();
        await activateTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        Await(engine.UnregisterObjectAsync(host)).Should().BeTrue();
    }

    [Fact]
    public async Task ActivateAsync_WhenActivationHookIsRunning_DoesNotExposeActiveStateEarly()
    {
        var host = new ObservingActivateObject();
        CreateEngineWith(host);

        Task activateTask = host.ActivateAsync().AsTask();
        await host.ActivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.ObservedState.Should().Be(MicroLifeCycleState.Initialized);
        host.ObservedIsActive.Should().BeFalse();
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);

        host.AllowActivation();
        await activateTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
    }

    [Fact]
    public async Task ActivateAsync_WhenCalledConcurrentlyOnSameComponent_RunsInitializeAndActivateHooksOnce()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync(new BlockingActivateComponent()));

        Task firstActivation = Task.Run(() => Await(component.ActivateAsync()));
        await component.ActivationStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Task secondActivation = Task.Run(() => Await(component.ActivateAsync()));
        await Task.Delay(100);

        component.InitializedCount.Should().Be(1);
        component.ActivatedCount.Should().Be(1);
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);

        component.AllowActivation();
        await Task.WhenAll(firstActivation, secondActivation).WaitAsync(TimeSpan.FromSeconds(5));

        component.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        component.InitializedCount.Should().Be(1);
        component.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public void Activate_WhenRollbackCallbackFails_ThrowsAggregateAndPreservesFailedRollbackState()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        var rollbackFailure = Await(host.AddComponentAsync<RollbackFailureComponent>());
        var failing = Await(host.AddComponentAsync<ThrowingComponent>());

        Action act = () => Await(host.ActivateAsync());

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().Contain(ex => ex.Message == "rollback deactivate failed");
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        rollbackFailure.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        failing.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);
    }

    [Fact]
    public void Activate_WhenOuterRollbackFails_PreservesOriginalActivationError()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        var component = Await(host.AddComponentAsync<OuterRollbackFailureComponent>());

        Action act = () => Await(host.ActivateAsync());

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "outer rollback failed");
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Initialized);
    }

    [Fact]
    public void AddComponent_WhenDetachRollbackFails_ThrowsAggregateAndDetachesComponent()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        Await(host.ActivateAsync());
        var component = new ThrowingAndDetachFailingComponent();

        Action act = () => Await(host.AddComponentAsync(component));

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "detach failed");
        host.GetComponent<ThrowingAndDetachFailingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Detached);
    }

    [Fact]
    public void RemoveComponent_WhenResolutionIsAmbiguous_DoesNotLeaveHostTransitioning()
    {
        var host = new MicroObject();
        Await(host.AddComponentAsync<AmbiguousComponentA>());
        Await(host.AddComponentAsync<AmbiguousComponentB>());

        Action act = () => Await(host.RemoveComponentAsync<AmbiguousComponentBase>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple components*");
        Await(host.AddComponentAsync<TrackingComponent>()).Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveComponentAsync_WhenDetachIsInProgress_ComponentRemainsVisibleUntilCompleted()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<BlockingDetachComponent>());

        Task removeTask = Task.Run(() => Await(host.RemoveComponentAsync(component)));
        await component.DetachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingDetachComponent>().Should().BeSameAs(component);
        host.Components.Should().ContainSingle().Which.Should().BeSameAs(component);

        component.AllowDetach();
        await removeTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingDetachComponent>().Should().BeNull();
        host.Components.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_ComponentInstance_RemovesItFromHostAndMarksItDisposed()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());

        Await(component.DisposeAsync());

        host.GetComponent<TrackingComponent>().Should().BeNull();
        host.Components.Should().BeEmpty();
        component.Host.Should().BeNull();
        component.IsDisposed.Should().BeTrue();
        component.DetachedCount.Should().Be(1);
        component.DisposedCount.Should().Be(1);

        Action act = () => Await(host.AddComponentAsync(component));

        act.Should().Throw<ObjectDisposedException>();
        Await(host.AddComponentAsync<TrackingComponent>()).Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_ComponentInstance_RemovesItFromHostAndMarksItDisposed()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());

        await component.DisposeAsync();

        host.GetComponent<TrackingComponent>().Should().BeNull();
        host.Components.Should().BeEmpty();
        component.Host.Should().BeNull();
        component.IsDisposed.Should().BeTrue();
        component.DetachedCount.Should().Be(1);
        component.DisposedCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_ComponentInstance_WhenDetachFails_StillMarksItDisposed()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<DetachAndDisposeTrackingComponent>());

        Action act = () => Await(component.DisposeAsync());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("detach failed");
        host.GetComponent<DetachAndDisposeTrackingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.IsDisposed.Should().BeTrue();
        component.DisposedCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_ComponentInstance_WhenHostIsTransitioning_DoesNotDisposeOrDetachComponent()
    {
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        Task addTask = Task.Run(() => Await(host.AddComponentAsync(component)));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => Await(component.DisposeAsync());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transition*");
        component.IsDisposed.Should().BeFalse();
        component.Host.Should().BeSameAs(host);

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingAttachComponent>().Should().BeSameAs(component);
        component.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Dispose_WhenAComponentDetachFails_ContinuesDetachingRemainingComponents()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();
        var tracking = Await(host.AddComponentAsync<TrackingComponent>());
        var failing = Await(host.AddComponentAsync<DetachFailingComponent>());

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Action act = () => Await(host.DisposeAsync());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("detach failed");
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        host.Engine.Should().BeNull();
        host.Components.Should().BeEmpty();
        tracking.Host.Should().BeNull();
        tracking.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        tracking.IsDisposed.Should().BeTrue();
        tracking.DisposedCount.Should().Be(1);
        failing.Host.Should().BeNull();
        failing.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        engine.Objects.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_HostDisposesComponents_InvokesOnDisposed()
    {
        var host = new MicroObject();
        var component = Await(host.AddComponentAsync<TrackingComponent>());

        Await(host.DisposeAsync());

        component.IsDisposed.Should().BeTrue();
        component.DisposedCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_WhenHostIsTransitioning_DoesNotDetachObjectFromEngine()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();

        Task addTask = Task.Run(() => Await(host.AddComponentAsync(component)));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => Await(host.DisposeAsync());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transition*");
        host.Engine.Should().BeSameAs(engine);
        engine.Objects.Should().Contain(host);
        host.LifeCycleState.Should().Be(MicroLifeCycleState.Attached);

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PublishAsync_SameObjectInvokesSubscribersInSubscriptionOrder()
    {
        var host = new MicroObject();
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add($"first:{domainEvent.Value}");
            return ValueTask.CompletedTask;
        });
        host.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add($"second:{domainEvent.Value}");
            return ValueTask.CompletedTask;
        });

        await host.PublishAsync(new TestDomainEvent("ready"));

        observed.Should().Equal("first:ready", "second:ready");
    }

    [Fact]
    public async Task PublishAsync_DoesNotPublishAcrossMicroObjects()
    {
        var firstHost = new MicroObject();
        var secondHost = new MicroObject();
        var observed = new List<string>();

        firstHost.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await secondHost.PublishAsync(new TestDomainEvent("ignored"));
        await firstHost.PublishAsync(new TestDomainEvent("local"));

        observed.Should().Equal("local");
    }

    [Fact]
    public async Task PublishAsync_UsesExactEventType()
    {
        var host = new MicroObject();
        var observed = new List<string>();

        host.Subscribe<BaseDomainEvent>((_, _) =>
        {
            observed.Add("base");
            return ValueTask.CompletedTask;
        });

        BaseDomainEvent polymorphicEvent = new DerivedDomainEvent();

        await host.PublishAsync(polymorphicEvent);
        await host.PublishAsync(new DerivedDomainEvent());
        await host.PublishAsync(new BaseDomainEvent());

        observed.Should().Equal("base");
    }

    [Fact]
    public async Task Subscribe_DisposeHandle_RemovesSubscriptionAndIsIdempotent()
    {
        var host = new MicroObject();
        var observed = new List<string>();

        IDisposable subscription = host.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await host.PublishAsync(new TestDomainEvent("before"));
        subscription.Dispose();
        subscription.Dispose();
        await host.PublishAsync(new TestDomainEvent("after"));

        observed.Should().Equal("before");
    }

    [Fact]
    public async Task PublishAsync_DoesNotHoldSubscriptionLockWhileAwaitingHandler()
    {
        var host = new MicroObject();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.Subscribe<TestDomainEvent>(async (_, cancellationToken) =>
        {
            handlerStarted.SetResult();
            await releaseHandler.Task.WaitAsync(cancellationToken);
        });

        Task publishTask = host.PublishAsync(new TestDomainEvent("waiting")).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<IDisposable> subscribeTask = Task.Run(() => host.Subscribe<TestDomainEvent>((_, _) => ValueTask.CompletedTask));
        IDisposable subscription = await subscribeTask.WaitAsync(TimeSpan.FromSeconds(5));

        subscription.Dispose();
        releaseHandler.SetResult();
        await publishTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MicroEvent_Clear_RemovesAllSubscriptions()
    {
        var events = new MicroEvent();
        var observed = new List<string>();

        events.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        events.Clear();
        await events.PublishAsync(new TestDomainEvent("after"));

        observed.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenMultipleHandlersThrow_AggregatesFailuresAndInvokesAllHandlers()
    {
        var host = new MicroObject();
        var firstFailure = new InvalidOperationException("first failed");
        var secondFailure = new ApplicationException("second failed");
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((_, _) => throw firstFailure);
        host.Subscribe<TestDomainEvent>((_, _) =>
        {
            observed.Add("middle");
            return ValueTask.CompletedTask;
        });
        host.Subscribe<TestDomainEvent>((_, _) => throw secondFailure);

        Func<Task> act = () => host.PublishAsync(new TestDomainEvent("aggregate")).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().Equal(firstFailure, secondFailure);
        observed.Should().Equal("middle");
    }

    [Fact]
    public async Task PublishAsync_WhenSingleHandlerThrows_RethrowsOriginalFailureAfterInvokingAllHandlers()
    {
        var host = new MicroObject();
        var failure = new InvalidOperationException("single failed");
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((_, _) => throw failure);
        host.Subscribe<TestDomainEvent>((_, _) =>
        {
            observed.Add("after-failure");
            return ValueTask.CompletedTask;
        });

        Func<Task> act = () => host.PublishAsync(new TestDomainEvent("single")).AsTask();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(failure);
        observed.Should().Equal("after-failure");
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerThrowsOperationCanceledWithoutRequestedToken_TreatsItAsOrdinaryFailure()
    {
        var host = new MicroObject();
        var failure = new OperationCanceledException("not requested");
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((_, _) => throw failure);
        host.Subscribe<TestDomainEvent>((_, _) =>
        {
            observed.Add("after-cancel-exception");
            return ValueTask.CompletedTask;
        });

        Func<Task> act = () => host.PublishAsync(new TestDomainEvent("not-canceled")).AsTask();

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.Should().BeSameAs(failure);
        observed.Should().Equal("after-cancel-exception");
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerThrowsOperationCanceledWithRequestedToken_StopsAndPropagatesCancellation()
    {
        var host = new MicroObject();
        using var cts = new CancellationTokenSource();
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((_, _) =>
        {
            observed.Add("before-cancel");
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        host.Subscribe<TestDomainEvent>((_, _) =>
        {
            observed.Add("after-cancel");
            return ValueTask.CompletedTask;
        });

        Func<Task> act = () => host.PublishAsync(new TestDomainEvent("canceled"), cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        observed.Should().Equal("before-cancel");
    }

    [Fact]
    public async Task PublishAsync_WhenTokenAlreadyCanceled_DoesNotInvokeHandlers()
    {
        var host = new MicroObject();
        using var cts = new CancellationTokenSource();
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await cts.CancelAsync();

        Func<Task> act = () => host.PublishAsync(new TestDomainEvent("canceled-before-publish"), cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        observed.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerPublishesAnotherEvent_AllowsReentrantPublish()
    {
        var host = new MicroObject();
        var observed = new List<string>();

        host.Subscribe<TestDomainEvent>(async (domainEvent, cancellationToken) =>
        {
            observed.Add($"outer:{domainEvent.Value}");
            await host.PublishAsync(new NestedDomainEvent($"nested:{domainEvent.Value}"), cancellationToken);
        });
        host.Subscribe<NestedDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await host.PublishAsync(new TestDomainEvent("root"));

        observed.Should().Equal("outer:root", "nested:root");
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static MicroEngine CreateEngineWith(MicroObject host)
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        Await(engine.RegisterObjectAsync(host)).Should().BeTrue();
        return engine;
    }

    private sealed class TrackingComponent : MicroComponent
    {
        public int InitializedCount { get; private set; }

        public int ActivatedCount { get; private set; }

        public int DetachedCount { get; private set; }

        public int DisposedCount { get; private set; }

        protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
        {
            InitializedCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            ActivatedCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
        {
            DetachedCount++;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
        {
            DisposedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RequiredComponent : MicroComponent
    {
    }

    private sealed class BlockingAttachComponent : MicroComponent
    {
        private readonly TaskCompletionSource _attachedStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _gate = new(false);

        public TaskCompletionSource AttachedStarted => _attachedStarted;

        public void AllowAttach() => _gate.Set();

        protected override ValueTask OnAttachedAsync(CancellationToken cancellationToken = default)
        {
            _attachedStarted.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Attach gate timed out.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingActivateObject : MicroObject
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public TaskCompletionSource ActivationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AllowActivation() => _gate.Set();

        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            ActivationStarted.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Activation gate timed out.");
            return base.OnActivatedAsync(cancellationToken);
        }
    }

    private sealed class ObservingActivateObject : MicroObject
    {
        private readonly TaskCompletionSource _activationReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ActivationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MicroLifeCycleState ObservedState { get; private set; }

        public bool ObservedIsActive { get; private set; }

        public void AllowActivation() => _activationReleased.TrySetResult();

        protected override async ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            ObservedState = LifeCycleState;
            ObservedIsActive = IsActive;
            ActivationStarted.TrySetResult();
            await _activationReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingActivateComponent : MicroComponent
    {
        private readonly TaskCompletionSource _activationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _activationReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _initializedCount;
        private int _activatedCount;

        public int InitializedCount => Volatile.Read(ref _initializedCount);

        public int ActivatedCount => Volatile.Read(ref _activatedCount);

        public Task ActivationStarted => _activationStarted.Task;

        public void AllowActivation() => _activationReleased.TrySetResult();

        protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _initializedCount);
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _activatedCount);
            _activationStarted.TrySetResult();
            await _activationReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingDetachComponent : MicroComponent
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public TaskCompletionSource DetachedStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AllowDetach() => _gate.Set();

        protected override ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
        {
            DetachedStarted.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Detach gate timed out.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingComponent : MicroComponent
    {
        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("activate failed"));
    }

    private sealed class RollbackFailureComponent : MicroComponent
    {
        protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("rollback deactivate failed"));
    }

    private sealed class ThrowingAndDetachFailingComponent : MicroComponent
    {
        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("activate failed"));

        protected override ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("detach failed"));
    }

    private sealed class OuterRollbackFailureComponent : MicroComponent
    {
        protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("activate failed"));

        protected override ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("outer rollback failed"));
    }

    private abstract class AmbiguousComponentBase : MicroComponent;

    private sealed class AmbiguousComponentA : AmbiguousComponentBase;

    private sealed class AmbiguousComponentB : AmbiguousComponentBase;

    private sealed class DetachFailingComponent : MicroComponent
    {
        protected override ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("detach failed"));
    }

    private sealed class DetachAndDisposeTrackingComponent : MicroComponent
    {
        public int DisposedCount { get; private set; }

        protected override ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("detach failed"));

        protected override ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
        {
            DisposedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantComponent : MicroComponent
    {
        protected override async ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        {
            await AddComponentAsync<TrackingComponent>(cancellationToken);
        }
    }

    private sealed record TestDomainEvent(string Value);

    private sealed record NestedDomainEvent(string Value);

    private class BaseDomainEvent;

    private sealed class DerivedDomainEvent : BaseDomainEvent;
}