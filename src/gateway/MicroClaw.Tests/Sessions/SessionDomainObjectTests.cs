using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// Session 聚合根领域对象单元测试（O-1-11）。
/// 覆盖状态转换方法、Pet 关联、领域事件队列机制。
/// 不依赖 SessionStore / 文件系统。
/// </summary>
public sealed class SessionDomainObjectTests
{
    // ── Session.Create ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_ReturnsSessionWithCorrectDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        var session = Session.Create("id1", "My Session", "provider-1",
            ChannelType.Web, "web", now);

        session.Id.Should().Be("id1");
        session.Title.Should().Be("My Session");
        session.ProviderId.Should().Be("provider-1");
        session.ChannelType.Should().Be(ChannelType.Web);
        session.ChannelId.Should().Be("web");
        session.CreatedAt.Should().Be(now);
        session.IsApproved.Should().BeFalse();
        session.Pet.Should().BeNull();
        session.AgentId.Should().BeNull();
        session.ParentSessionId.Should().BeNull();
        session.ApprovalReason.Should().BeNull();
    }

    [Fact]
    public void Create_WithOptionalParams_PreservesAll()
    {
        var session = Session.Create("id2", "T", "p1", ChannelType.Feishu, "fs",
            DateTimeOffset.UtcNow, agentId: "agent-1", parentSessionId: "parent-99");

        session.AgentId.Should().Be("agent-1");
        session.ParentSessionId.Should().Be("parent-99");
    }

    // ── Session.Reconstitute ───────────────────────────────────────────────────

    [Fact]
    public void Reconstitute_PreservesAllPropertiesWithoutRaisingEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var session = Session.Reconstitute(
            id: "r1", title: "Restored", providerId: "p2",
            isApproved: true, channelType: ChannelType.WeCom, channelId: "wc",
            createdAt: now, agentId: "a1", parentSessionId: "p0", approvalReason: "ok");

        session.Id.Should().Be("r1");
        session.IsApproved.Should().BeTrue();
        session.ApprovalReason.Should().Be("ok");
        session.ChannelType.Should().Be(ChannelType.WeCom);
        session.PopDomainEvents().Should().BeEmpty("Reconstitute 不触发领域事件");
    }

    // ── Session.Approve ────────────────────────────────────────────────────────

    [Fact]
    public void Approve_SetsIsApprovedTrueAndSetsReason()
    {
        var session = Session.Create("s1", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.Approve("test reason");

        session.IsApproved.Should().BeTrue();
        session.ApprovalReason.Should().Be("test reason");
    }

    [Fact]
    public void Approve_RaisesExactlyOneSessionApprovedEvent()
    {
        var session = Session.Create("s2", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.Approve();

        var events = session.PopDomainEvents();
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<SessionApprovedEvent>()
            .Which.SessionId.Should().Be("s2");
    }

    [Fact]
    public void Approve_CalledTwice_RaisesTwoEvents()
    {
        var session = Session.Create("s3", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.Approve("first");
        session.Approve("second");

        session.PopDomainEvents().Should().HaveCount(2);
    }

    // ── Session.Disable ────────────────────────────────────────────────────────

    [Fact]
    public void Disable_SetsIsApprovedFalseAndUpdatesReason()
    {
        var session = Session.Reconstitute("d1", "T", "p1", true, ChannelType.Web, "w",
            DateTimeOffset.UtcNow, approvalReason: "old");

        session.Disable("disabled by admin");

        session.IsApproved.Should().BeFalse();
        session.ApprovalReason.Should().Be("disabled by admin");
    }

    [Fact]
    public void Disable_DoesNotRaiseDomainEvent()
    {
        var session = Session.Reconstitute("d2", "T", "p1", true, ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.Disable();

        session.PopDomainEvents().Should().BeEmpty("Disable 是管理操作，不触发领域事件");
    }

    // ── Session.UpdateProvider ─────────────────────────────────────────────────

    [Fact]
    public void UpdateProvider_ChangesProviderId()
    {
        var session = Session.Create("sp1", "T", "old-provider", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.UpdateProvider("new-provider");

        session.ProviderId.Should().Be("new-provider");
    }

    [Fact]
    public void UpdateProvider_RaisesSessionProviderChangedEvent()
    {
        var session = Session.Create("sp2", "T", "old-p", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.UpdateProvider("new-p");

        var events = session.PopDomainEvents();
        events.Should().HaveCount(1);
        var evt = events[0].Should().BeOfType<SessionProviderChangedEvent>().Subject;
        evt.SessionId.Should().Be("sp2");
        evt.OldProviderId.Should().Be("old-p");
        evt.NewProviderId.Should().Be("new-p");
    }

    // ── Session.UpdateTitle ────────────────────────────────────────────────────

    [Fact]
    public void UpdateTitle_ChangesTitle_NoEvent()
    {
        var session = Session.Create("ut1", "Old Title", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);

        session.UpdateTitle("New Title");

        session.Title.Should().Be("New Title");
        session.PopDomainEvents().Should().BeEmpty();
    }

    // ── Session.AttachPet / DetachPet ──────────────────────────────────────────

    [Fact]
    public void AttachPet_SetsPetContext()
    {
        var session = Session.Create("ap1", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);
        var petCtx = Substitute.For<IPetContext>();

        session.AttachPet(petCtx);

        session.Pet.Should().BeSameAs(petCtx);
    }

    [Fact]
    public void DetachPet_ClearsPetContext()
    {
        var session = Session.Create("dp1", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);
        session.AttachPet(Substitute.For<IPetContext>());

        session.DetachPet();

        session.Pet.Should().BeNull();
    }

    // ── Session.PopDomainEvents ────────────────────────────────────────────────

    [Fact]
    public void PopDomainEvents_ReturnsSnapshot_AndClearsQueue()
    {
        var session = Session.Create("pe1", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);
        session.Approve();
        session.UpdateProvider("p2");

        var firstPop = session.PopDomainEvents();
        var secondPop = session.PopDomainEvents();

        firstPop.Should().HaveCount(2);
        secondPop.Should().BeEmpty("弹出后队列应清空");
    }

    [Fact]
    public void PopDomainEvents_ReturnedList_IsImmutable()
    {
        var session = Session.Create("pe2", "T", "p1", ChannelType.Web, "w", DateTimeOffset.UtcNow);
        session.Approve();

        var events = session.PopDomainEvents();

        // IReadOnlyList 不应直接暴露内部可变集合
        events.Should().BeAssignableTo<IReadOnlyList<MicroClaw.Abstractions.Events.IDomainEvent>>();
    }

    // ── Session.ToInfo ─────────────────────────────────────────────────────────

    [Fact]
    public void ToInfo_MapsAllFieldsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var session = Session.Reconstitute("ti1", "Title", "prov", true, ChannelType.Feishu, "fs",
            now, "agent-0", "parent-0", "reason-0");

        var info = session.ToInfo();

        info.Id.Should().Be("ti1");
        info.Title.Should().Be("Title");
        info.ProviderId.Should().Be("prov");
        info.IsApproved.Should().BeTrue();
        info.ChannelType.Should().Be(ChannelType.Feishu);
        info.ChannelId.Should().Be("fs");
        info.CreatedAt.Should().Be(now);
        info.AgentId.Should().Be("agent-0");
        info.ParentSessionId.Should().Be("parent-0");
        info.ApprovalReason.Should().Be("reason-0");
    }
}
