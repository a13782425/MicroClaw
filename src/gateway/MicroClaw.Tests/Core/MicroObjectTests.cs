using FluentAssertions;
using MicroClaw.Core;
using System.Collections.Concurrent;

namespace MicroClaw.Tests.Core;

public sealed class MicroObjectTests
{
    [Fact]
    public void AddComponent_ComponentAlreadyAttachedToAnotherHost_Throws()
    {
        var firstHost = new MicroObject();
        var secondHost = new MicroObject();
        var component = new TrackingComponent();

        firstHost.AddComponent(component);

        Action act = () => secondHost.AddComponent(component);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*one MicroObject*");
    }

    [Fact]
    public void GetAddRemoveComponent_SameTypeRoundTrip_Works()
    {
        var host = new MicroObject();

        var component = host.AddComponent<TrackingComponent>();

        host.GetComponent<TrackingComponent>().Should().BeSameAs(component);
        component.GetComponent<TrackingComponent>().Should().BeSameAs(component);

        host.RemoveComponent<TrackingComponent>().Should().BeTrue();
        host.GetComponent<TrackingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.State.Should().Be(MicroComponentState.Detached);
        component.DetachedCount.Should().Be(1);
    }

    [Fact]
    public void AddComponent_HostAlreadyActive_InitializesAndActivatesComponent()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        host.Activate();

        var component = host.AddComponent<TrackingComponent>();

        component.State.Should().Be(MicroComponentState.Active);
        component.InitializedCount.Should().Be(1);
        component.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public void AddComponent_WhenActivationFails_RollsBackAttachment()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        host.Activate();
        var component = new ThrowingComponent();

        Action act = () => host.AddComponent(component);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("activate failed");
        host.GetComponent<ThrowingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.State.Should().Be(MicroComponentState.Detached);
    }

    [Fact]
    public async Task AddComponent_WhenAnotherThreadRemovesDuringAttach_RejectsConcurrentMutation()
    {
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        Task addTask = Task.Run(() => host.AddComponent(component));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => host.RemoveComponent(component);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lifecycle transition*");

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));

        host.GetComponent<BlockingAttachComponent>().Should().BeSameAs(component);
        component.Host.Should().BeSameAs(host);
    }

    [Fact]
    public void Activate_WhenComponentMutatesHost_ReentrantMutationIsRejected()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        host.AddComponent<ReentrantComponent>();

        Action act = () => host.Activate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*lifecycle transition*");
        host.GetComponent<TrackingComponent>().Should().BeNull();
        host.State.Should().Be(MicroObjectState.Created);
    }

    [Fact]
    public void Activate_WhenRollbackCallbackFails_ThrowsAggregateAndPreservesFailedRollbackState()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        var rollbackFailure = host.AddComponent<RollbackFailureComponent>();
        var failing = host.AddComponent<ThrowingComponent>();

        Action act = () => host.Activate();

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().Contain(ex => ex.Message == "rollback deactivate failed");
        host.State.Should().Be(MicroObjectState.Active);
        rollbackFailure.State.Should().Be(MicroComponentState.Active);
        failing.State.Should().Be(MicroComponentState.Attached);
    }

    [Fact]
    public void Activate_WhenOuterRollbackFails_PreservesOriginalActivationError()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        var component = host.AddComponent<OuterRollbackFailureComponent>();

        Action act = () => host.Activate();

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "outer rollback failed");
        host.State.Should().Be(MicroObjectState.Initialized);
        component.State.Should().Be(MicroComponentState.Initialized);
    }

    [Fact]
    public void AddComponent_WhenDetachRollbackFails_ThrowsAggregateAndDetachesComponent()
    {
        var host = new MicroObject();
        CreateEngineWith(host);
        host.Activate();
        var component = new ThrowingAndDetachFailingComponent();

        Action act = () => host.AddComponent(component);

        var exception = act.Should().Throw<AggregateException>().Which;
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "activate failed");
        exception.InnerExceptions.Should().ContainSingle(ex => ex.Message == "detach failed");
        host.GetComponent<ThrowingAndDetachFailingComponent>().Should().BeNull();
        component.Host.Should().BeNull();
        component.State.Should().Be(MicroComponentState.Detached);
    }

    [Fact]
    public void RemoveComponent_WhenResolutionIsAmbiguous_DoesNotLeaveHostTransitioning()
    {
        var host = new MicroObject();
        host.AddComponent<AmbiguousComponentA>();
        host.AddComponent<AmbiguousComponentB>();

        Action act = () => host.RemoveComponent<AmbiguousComponentBase>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple components*");
        host.AddComponent<TrackingComponent>().Should().NotBeNull();
    }

    [Fact]
    public void Dispose_ComponentInstance_RemovesItFromHostAndMarksItDisposed()
    {
        var host = new MicroObject();
        var component = host.AddComponent<TrackingComponent>();

        component.Dispose();

        host.GetComponent<TrackingComponent>().Should().BeNull();
        host.Components.Should().BeEmpty();
        component.Host.Should().BeNull();
        component.IsDisposed.Should().BeTrue();
        component.DetachedCount.Should().Be(1);
        component.DisposedCount.Should().Be(1);

        Action act = () => host.AddComponent(component);

        act.Should().Throw<ObjectDisposedException>();
        host.AddComponent<TrackingComponent>().Should().NotBeNull();
    }

    [Fact]
    public void Dispose_ComponentInstance_WhenDetachFails_StillMarksItDisposed()
    {
        var host = new MicroObject();
        var component = host.AddComponent<DetachAndDisposeTrackingComponent>();

        Action act = () => component.Dispose();

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

        Task addTask = Task.Run(() => host.AddComponent(component));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => component.Dispose();

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
        var tracking = host.AddComponent<TrackingComponent>();
        var failing = host.AddComponent<DetachFailingComponent>();

        engine.RegisterObject(host).Should().BeTrue();

        Action act = () => host.Dispose();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("detach failed");
        host.State.Should().Be(MicroObjectState.Disposed);
        host.Engine.Should().BeNull();
        host.Components.Should().BeEmpty();
        tracking.Host.Should().BeNull();
        tracking.State.Should().Be(MicroComponentState.Detached);
        tracking.IsDisposed.Should().BeTrue();
        tracking.DisposedCount.Should().Be(1);
        failing.Host.Should().BeNull();
        failing.State.Should().Be(MicroComponentState.Detached);
        engine.Objects.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_HostDisposesComponents_InvokesOnDisposed()
    {
        var host = new MicroObject();
        var component = host.AddComponent<TrackingComponent>();

        host.Dispose();

        component.IsDisposed.Should().BeTrue();
        component.DisposedCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_WhenHostIsTransitioning_DoesNotDetachObjectFromEngine()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var host = new MicroObject();
        var component = new BlockingAttachComponent();

        engine.RegisterObject(host).Should().BeTrue();

        Task addTask = Task.Run(() => host.AddComponent(component));
        await component.AttachedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Action act = () => host.Dispose();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transition*");
        host.Engine.Should().BeSameAs(engine);
        engine.Objects.Should().Contain(host);
        host.State.Should().Be(MicroObjectState.Created);

        component.AllowAttach();
        await addTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static MicroEngine CreateEngineWith(MicroObject host)
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        engine.RegisterObject(host).Should().BeTrue();
        return engine;
    }

    private sealed class TrackingComponent : MicroComponent
    {
        public int InitializedCount { get; private set; }

        public int ActivatedCount { get; private set; }

        public int DetachedCount { get; private set; }

        public int DisposedCount { get; private set; }

        protected override void OnInitialized()
        {
            InitializedCount++;
        }

        protected override void OnActivated()
        {
            ActivatedCount++;
        }

        protected override void OnDetached()
        {
            DetachedCount++;
        }

        protected override void OnDisposed()
        {
            DisposedCount++;
        }
    }

    private sealed class BlockingAttachComponent : MicroComponent
    {
        private readonly TaskCompletionSource _attachedStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _gate = new(false);

        public TaskCompletionSource AttachedStarted => _attachedStarted;

        public void AllowAttach() => _gate.Set();

        protected override void OnAttached()
        {
            _attachedStarted.TrySetResult();
            _gate.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ThrowingComponent : MicroComponent
    {
        protected override void OnActivated()
        {
            throw new InvalidOperationException("activate failed");
        }
    }

    private sealed class RollbackFailureComponent : MicroComponent
    {
        protected override void OnDeactivated()
        {
            throw new InvalidOperationException("rollback deactivate failed");
        }
    }

    private sealed class ThrowingAndDetachFailingComponent : MicroComponent
    {
        protected override void OnActivated()
        {
            throw new InvalidOperationException("activate failed");
        }

        protected override void OnDetached()
        {
            throw new InvalidOperationException("detach failed");
        }
    }

    private sealed class OuterRollbackFailureComponent : MicroComponent
    {
        protected override void OnActivated()
        {
            throw new InvalidOperationException("activate failed");
        }

        protected override void OnUninitialized()
        {
            throw new InvalidOperationException("outer rollback failed");
        }
    }

    private abstract class AmbiguousComponentBase : MicroComponent;

    private sealed class AmbiguousComponentA : AmbiguousComponentBase;

    private sealed class AmbiguousComponentB : AmbiguousComponentBase;

    private sealed class DetachFailingComponent : MicroComponent
    {
        protected override void OnDetached()
        {
            throw new InvalidOperationException("detach failed");
        }
    }

    private sealed class DetachAndDisposeTrackingComponent : MicroComponent
    {
        public int DisposedCount { get; private set; }

        protected override void OnDetached()
        {
            throw new InvalidOperationException("detach failed");
        }

        protected override void OnDisposed()
        {
            DisposedCount++;
        }
    }

    private sealed class ReentrantComponent : MicroComponent
    {
        protected override void OnActivated()
        {
            AddComponent<TrackingComponent>();
        }
    }
}