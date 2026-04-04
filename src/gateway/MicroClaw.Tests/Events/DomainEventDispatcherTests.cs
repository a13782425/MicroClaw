using FluentAssertions;
using MicroClaw.Abstractions.Events;
using MicroClaw.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tests.Events;

/// <summary>
/// DomainEventDispatcher 单元测试（O-1-11）。
/// 使用真实 DI 容器（ServiceCollection），无需 mocking framework。
/// </summary>
public sealed class DomainEventDispatcherTests
{
    // ── 辅助：内部测试事件类型 ──────────────────────────────────────────────────
    private sealed record TestEvent(string Value) : IDomainEvent;
    private sealed record OtherEvent : IDomainEvent;

    // ── 辅助：可追踪调用次数的处理器 ────────────────────────────────────────────
    private sealed class TrackingHandler(Action<TestEvent>? callback = null)
        : IDomainEventHandler<TestEvent>
    {
        public int CallCount { get; private set; }
        public TestEvent? LastEvent { get; private set; }

        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
        {
            CallCount++;
            LastEvent = domainEvent;
            callback?.Invoke(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated handler failure");
    }

    // ── 辅助：构建带有指定处理器的 ServiceProvider ──────────────────────────────
    private static IServiceProvider BuildSp(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ── DispatchAsync with registered handler ─────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_CallsHandlerOnce()
    {
        var handler = new TrackingHandler();
        var sp = BuildSp(svc => svc.AddSingleton<IDomainEventHandler<TestEvent>>(handler));
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("hello"));

        handler.CallCount.Should().Be(1);
        handler.LastEvent!.Value.Should().Be("hello");
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_CallsAllInOrder()
    {
        var order = new List<int>();
        var h1 = new TrackingHandler(_ => order.Add(1));
        var h2 = new TrackingHandler(_ => order.Add(2));
        var sp = BuildSp(svc =>
        {
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(h1);
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(h2);
        });
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("x"));

        h1.CallCount.Should().Be(1);
        h2.CallCount.Should().Be(1);
        order.Should().Equal(1, 2);
    }

    // ── Exception isolation ───────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WhenFirstHandlerThrows_ContinuesWithSecondHandler()
    {
        var goodHandler = new TrackingHandler();
        var sp = BuildSp(svc =>
        {
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(new ThrowingHandler());
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(goodHandler);
        });
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        // 不抛异常（DomainEventDispatcher 捕获并记录日志，不中断主流程）
        var act = async () => await dispatcher.DispatchAsync(new TestEvent("y"));
        await act.Should().NotThrowAsync();

        goodHandler.CallCount.Should().Be(1,
            because: "throwing handler 不应阻止后续 handler 执行");
    }

    [Fact]
    public async Task DispatchAsync_WhenAllHandlersThrow_DoesNotThrow()
    {
        var sp = BuildSp(svc =>
        {
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(new ThrowingHandler());
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(new ThrowingHandler());
        });
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var act = async () => await dispatcher.DispatchAsync(new TestEvent("z"));
        await act.Should().NotThrowAsync();
    }

    // ── No handlers ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WithNoHandlers_DoesNotThrow()
    {
        var sp = BuildSp(_ => { });
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var act = async () => await dispatcher.DispatchAsync(new TestEvent("empty"));
        await act.Should().NotThrowAsync();
    }

    // ── Handler for other event type is not called ────────────────────────────

    [Fact]
    public async Task DispatchAsync_HandlerForOtherEventType_IsNotCalled()
    {
        int called = 0;
        var sp = BuildSp(svc =>
        {
            // 仅注册 OtherEvent 处理器，分发 TestEvent 时不应被触发
            svc.AddSingleton<IDomainEventHandler<OtherEvent>>(
                new DelegateHandler<OtherEvent>(_ => called++));
        });
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("not-other"));

        called.Should().Be(0);
    }

    // ── CancellationToken is passed through ───────────────────────────────────

    [Fact]
    public async Task DispatchAsync_PassesCancellationTokenToHandler()
    {
        CancellationToken captured = default;
        using var cts = new CancellationTokenSource();
        var sp = BuildSp(svc =>
            svc.AddSingleton<IDomainEventHandler<TestEvent>>(
                new DelegateHandler<TestEvent>((_, ct) => { captured = ct; return Task.CompletedTask; })));
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("ct-test"), cts.Token);

        captured.Should().Be(cts.Token);
    }

    // ── 辅助：委托处理器 ────────────────────────────────────────────────────────
    private sealed class DelegateHandler<T>(Func<T, CancellationToken, Task> func)
        : IDomainEventHandler<T> where T : IDomainEvent
    {
        public DelegateHandler(Action<T> action)
            : this((evt, _) => { action(evt); return Task.CompletedTask; }) { }

        public Task HandleAsync(T domainEvent, CancellationToken ct = default)
            => func(domainEvent, ct);
    }
}
