using FluentAssertions;
using MicroClaw.Core;

namespace MicroClaw.Tests.Core;

public sealed class MicroEventHostTests
{
    [Fact]
    public async Task MicroEngineEvent_PublishAsync_InvokesEngineSubscribersOnly()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);
        var service = new TrackingService(order: 10);
        var observed = new List<string>();

        engine.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add($"engine:{domainEvent.Value}");
            return ValueTask.CompletedTask;
        });
        service.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add($"service:{domainEvent.Value}");
            return ValueTask.CompletedTask;
        });

        await service.PublishAsync(new TestDomainEvent("ignored"));
        await engine.PublishAsync(new TestDomainEvent("local"));

        observed.Should().Equal("service:ignored", "engine:local");
    }

    [Fact]
    public async Task MicroServiceEvent_PublishAsync_InvokesServiceSubscribersOnly()
    {
        var firstService = new TrackingService(order: 10);
        var secondService = new TrackingService(order: 20);
        var observed = new List<string>();

        firstService.Subscribe<TestDomainEvent>((domainEvent, _) =>
        {
            observed.Add(domainEvent.Value);
            return ValueTask.CompletedTask;
        });

        await secondService.PublishAsync(new TestDomainEvent("ignored"));
        await firstService.PublishAsync(new TestDomainEvent("local"));

        observed.Should().Equal("local");
    }

    [Fact]
    public async Task MicroEngineEvent_SubscribeAfterDispose_ThrowsObjectDisposedException()
    {
        var engine = new MicroEngine(new NullServiceProvider(), []);

        await engine.DisposeAsync();

        Action act = () => engine.Subscribe<TestDomainEvent>((_, _) => ValueTask.CompletedTask);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task MicroServiceEvent_SubscribeAfterDispose_ThrowsObjectDisposedException()
    {
        var service = new TrackingService(order: 10);

        await service.DisposeAsync();

        Action act = () => service.Subscribe<TestDomainEvent>((_, _) => ValueTask.CompletedTask);

        act.Should().Throw<ObjectDisposedException>();
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TrackingService(int order) : MicroService
    {
        public override int Order => order;
    }

    private sealed record TestDomainEvent(string Value);
}