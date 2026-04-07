using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Events;
using MicroClaw.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// O-4-6：Session 完整生命周期端到端集成测试。
/// <para>
/// 覆盖范围：
/// <list type="bullet">
///   <item>创建 Session → Approve → 领域事件触发 → PetContext 附加到 Session</item>
///   <item>Session 删除 → 领域事件触发 → PetContext.Dispose() 被调用</item>
///   <item>整个流程使用真实 DomainEventDispatcher + Session 领域对象（内存 ISessionRepository 替代真实持久化）</item>
/// </list>
/// </para>
/// </summary>
public sealed class SessionLifecycleIntegrationTests
{
    // ── 辅助：可追踪调用次数的 PetContext Stub ──────────────────────────────────

    private sealed class TrackablePetContext : IPetContext, IDisposable
    {
        public PetContextState State { get; private set; } = PetContextState.Active;
        public bool IsEnabled => State == PetContextState.Active;
        public bool DisposeCalled { get; private set; }

        public void MarkDirty() { }
        public void Dispose()
        {
            DisposeCalled = true;
            State = PetContextState.Disabled;
        }
    }

    // ── 辅助：构建内存 ISessionRepository（NSubstitute + 字典）─────────────────

    private static ISessionRepository BuildInMemoryRepo()
    {
        var store = new Dictionary<string, Session>(StringComparer.Ordinal);
        var repo = Substitute.For<ISessionRepository>();

        repo.Get(Arg.Any<string>()).Returns(ci =>
            store.TryGetValue(ci.Arg<string>(), out var s) ? s : null);
        repo.When(r => r.Save(Arg.Any<Session>()))
            .Do(ci => store[ci.Arg<Session>().Id] = ci.Arg<Session>());
        repo.When(r => r.Delete(Arg.Any<string>()))
            .Do(ci => store.Remove(ci.Arg<string>()));

        return repo;
    }

    // ── 辅助：在 SessionApprovedEvent 时附加 PetContext ────────────────────────

    private sealed class FakePetApprovalHandler(ISessionRepository repo, TrackablePetContext ctx)
        : IDomainEventHandler<SessionApprovedEvent>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(SessionApprovedEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            repo.Get(domainEvent.SessionId)?.AttachPet(ctx);
            return Task.CompletedTask;
        }
    }

    // ── 辅助：在 SessionDeletedEvent 时 Dispose PetContext ─────────────────────

    private sealed class FakePetDeletionHandler(ISessionRepository repo)
        : IDomainEventHandler<SessionDeletedEvent>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(SessionDeletedEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            var session = repo.Get(domainEvent.SessionId);
            if (session?.Pet is IDisposable disposable)
            {
                disposable.Dispose();
                session.DetachPet();
            }
            return Task.CompletedTask;
        }
    }

    // ── 辅助：构建 ServiceProvider ────────────────────────────────────────────

    private static (IServiceProvider sp, ISessionRepository repo,
        FakePetApprovalHandler approvalHandler, FakePetDeletionHandler deletionHandler)
        BuildServices(TrackablePetContext petCtx)
    {
        var repo = BuildInMemoryRepo();
        var approvalHandler = new FakePetApprovalHandler(repo, petCtx);
        var deletionHandler = new FakePetDeletionHandler(repo);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<IDomainEventHandler<SessionApprovedEvent>>(approvalHandler);
        services.AddSingleton<IDomainEventHandler<SessionDeletedEvent>>(deletionHandler);
        services.AddSingleton(approvalHandler);
        services.AddSingleton(deletionHandler);
        var sp = services.BuildServiceProvider();
        return (sp, repo, approvalHandler, deletionHandler);
    }

    // ── 辅助：直接用 Session.Reconstitute 创建并保存（无需 SessionStore / MicroClawConfig）

    private static Session CreateAndSaveSession(
        ISessionRepository repo, string title = "测试会话", string providerId = "provider1")
    {
        var session = Session.Reconstitute(
            id: Guid.NewGuid().ToString("N")[..12],
            title: title,
            providerId: providerId,
            isApproved: false,
            channelType: ChannelType.Web,
            channelId: "",
            createdAtMs: DateTimeOffset.UtcNow);
        repo.Save(session);
        return session;
    }

    // ── 辅助：按具体类型分发领域事件（IDomainEventDispatcher 泛型推断必须使用具体类型）

    private static async Task DispatchAllEvents(
        IDomainEventDispatcher dispatcher,
        IReadOnlyList<IDomainEvent> events,
        CancellationToken ct = default)
    {
        foreach (var ev in events)
        {
            // 必须转型到具体类型，否则 TEvent = IDomainEvent → GetServices<IDomainEventHandler<IDomainEvent>> 返回空
            if (ev is SessionApprovedEvent approvedEvt)
                await dispatcher.DispatchAsync(approvedEvt, ct);
            else if (ev is SessionDeletedEvent deletedEvt)
                await dispatcher.DispatchAsync(deletedEvt, ct);
            else if (ev is SessionProviderChangedEvent changedEvt)
                await dispatcher.DispatchAsync(changedEvt, ct);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  生命周期：创建 → 审批 → PetContext 附加
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Approve_DispatchesEvent_AndHandlerAttachesPetContext()
    {
        var petCtx = new TrackablePetContext();
        var (sp, repo, approvalHandler, _) = BuildServices(petCtx);
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        // 1. 创建并保存 Session
        var session = CreateAndSaveSession(repo);
        session.IsApproved.Should().BeFalse();

        // 2. 审批（产生领域事件）
        session.Approve("集成测试审批");
        repo.Save(session);

        // 3. 分发领域事件
        await DispatchAllEvents(dispatcher, session.PopDomainEvents());

        // 4. 验证
        approvalHandler.WasCalled.Should().BeTrue("审批事件处理器应被调用");
        session.Pet.Should().NotBeNull("Pet 应在审批后附加到 Session");
        session.Pet!.IsEnabled.Should().BeTrue("Pet 应处于启用状态");
    }

    [Fact]
    public async Task Approve_ThenReloadSession_SessionIsApproved()
    {
        var petCtx = new TrackablePetContext();
        var (sp, repo, _, _) = BuildServices(petCtx);
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var session = CreateAndSaveSession(repo, "重载测试");
        session.Approve("自动审批");
        repo.Save(session);
        foreach (var ev in session.PopDomainEvents())
            await DispatchAllEvents(dispatcher, [ev]);

        // 从 in-memory 仓储重新获取
        var reloaded = repo.Get(session.Id);
        reloaded.Should().NotBeNull();
        reloaded!.IsApproved.Should().BeTrue("审批状态应持久化");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  生命周期：删除 → PetContext Dispose
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_DispatchesEvent_AndHandlerDisposesPetContext()
    {
        var petCtx = new TrackablePetContext();
        var (sp, repo, _, deletionHandler) = BuildServices(petCtx);
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        // 创建并附加 PetContext
        var session = CreateAndSaveSession(repo, "删除测试");
        session.Approve("审批");
        session.AttachPet(petCtx);
        repo.Save(session);

        // 发布 SessionDeletedEvent（在删除前）
        await dispatcher.DispatchAsync(new SessionDeletedEvent(session.Id));
        repo.Delete(session.Id);

        deletionHandler.WasCalled.Should().BeTrue("删除事件处理器应被调用");
        petCtx.DisposeCalled.Should().BeTrue("PetContext 应在 Session 删除时被 Dispose");
        petCtx.State.Should().Be(PetContextState.Disabled, "PetContext 状态应变为 Disabled");
    }

    [Fact]
    public async Task Delete_AfterDispose_SessionNoLongerExistsInRepo()
    {
        var petCtx = new TrackablePetContext();
        var (sp, repo, _, _) = BuildServices(petCtx);
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var session = CreateAndSaveSession(repo, "最终删除测试");
        session.Approve("test");
        session.AttachPet(petCtx);
        repo.Save(session);

        await dispatcher.DispatchAsync(new SessionDeletedEvent(session.Id));
        repo.Delete(session.Id);

        repo.Get(session.Id).Should().BeNull("Session 删除后 repo.Get 应返回 null");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  生命周期：发送消息（通过 Session 获取 ProviderId / ChannelType）
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Session_ProvidesContextForAgentRunner()
    {
        var petCtx = new TrackablePetContext();
        var (_, repo, _, _) = BuildServices(petCtx);

        var session = CreateAndSaveSession(repo, "消息路由测试", "gpt4o-prod");
        session.Approve("test");
        session.AttachPet(petCtx);
        repo.Save(session);

        var retrieved = repo.Get(session.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ProviderId.Should().Be("gpt4o-prod",
            "AgentRunner 依赖 ProviderId 路由到正确 LLM 提供商");
        retrieved.IsApproved.Should().BeTrue("只有已审批 Session 才能处理消息");
        retrieved.Pet.Should().NotBeNull("已审批 Session 应有 Pet 可供 PetRunner 使用");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  完整生命周期一体测试
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_NoExceptions_AllStatesCorrect()
    {
        var petCtx = new TrackablePetContext();
        var (sp, repo, approvalHandler, deletionHandler) = BuildServices(petCtx);
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        // 创建
        var session = CreateAndSaveSession(repo, "完整生命周期测试", "provider-full");
        session.IsApproved.Should().BeFalse();
        session.Pet.Should().BeNull();

        // 审批 + 事件分发（handler 会 AttachPet）
        session.Approve("admin");
        repo.Save(session);
        await DispatchAllEvents(dispatcher, session.PopDomainEvents());

        approvalHandler.WasCalled.Should().BeTrue();
        session.IsApproved.Should().BeTrue();
        session.ProviderId.Should().Be("provider-full");
        session.Pet.Should().NotBeNull();
        petCtx.IsEnabled.Should().BeTrue();

        // 删除 + 事件分发（handler 会 Dispose PetContext）
        await dispatcher.DispatchAsync(new SessionDeletedEvent(session.Id));
        repo.Delete(session.Id);

        deletionHandler.WasCalled.Should().BeTrue();
        repo.Get(session.Id).Should().BeNull();
        petCtx.DisposeCalled.Should().BeTrue();
        petCtx.State.Should().Be(PetContextState.Disabled);
    }
}
