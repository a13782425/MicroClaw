using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// SessionService 瀹炵幇 ISessionRepository 鐨勯泦鎴愭祴璇曘€?/// 姣忎釜娴嬭瘯浣跨敤鐙珛鐨勪复鏃剁洰褰曪紝鏂囦欢绯荤粺瀹屽叏闅旂銆?/// </summary>
public sealed class SessionRepositoryTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionService _svc;
    private readonly ISessionRepository _repo;

    public SessionRepositoryTests()
    {
        TestConfigFixture.EnsureInitialized();

        var hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var clients = Substitute.For<IHubClients>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(Substitute.For<IClientProxy>());

        var agentStore = new AgentStore();
        var webChannel = new WebSessionChannel(hubContext);
        _svc = new SessionService(agentStore, hubContext, [], webChannel, _tempDir.Path);
        _repo = _svc;
    }

    public void Dispose() => _tempDir.Dispose();

    // 鈹€鈹€ Get 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void Get_AfterCreate_ReturnsDomainObject()
    {
        var session = _svc.CreateSession("Test", "p1");

        Session? result = _repo.Get(session.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(session.Id);
        result.Title.Should().Be("Test");
        result.ProviderId.Should().Be("p1");
        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        _repo.Get("does-not-exist").Should().BeNull();
    }

    // 鈹€鈹€ GetAll 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void GetAll_ReturnsAllSessionsAsDomainObjects()
    {
        _svc.CreateSession("A", "p1");
        _svc.CreateSession("B", "p1");

        var all = _repo.GetAll();

        all.Should().HaveCount(2);
        all.Should().AllBeOfType<Session>();
    }

    // 鈹€鈹€ GetTopLevel 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void GetTopLevel_ExcludesSubAgentSessions()
    {
        _svc.CreateSession("Root", "p1");
        _svc.CreateSession("Sub", "p1", parentSessionId: "some-parent-id");

        var topLevel = _repo.GetTopLevel();

        topLevel.Should().HaveCount(1);
        topLevel[0].Title.Should().Be("Root");
    }

    // 鈹€鈹€ Save (update existing) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void Save_ExistingSession_PersistsChangedFields()
    {
        var created = _svc.CreateSession("Original", "p1");
        Session session = _repo.Get(created.Id)!;

        session.Approve("reason");
        _repo.Save(session);

        Session? reloaded = _repo.Get(created.Id);
        reloaded.Should().NotBeNull();
        reloaded!.IsApproved.Should().BeTrue();
        reloaded.ApprovalReason.Should().Be("reason");
    }

    [Fact]
    public void Save_ExistingSession_UpdatedProvider_IsPersisted()
    {
        var created = _svc.CreateSession("T", "old-p");
        Session session = _repo.Get(created.Id)!;

        session.UpdateProvider("new-p");
        session.PopDomainEvents();
        _repo.Save(session);

        Session? reloaded = _repo.Get(created.Id);
        reloaded!.ProviderId.Should().Be("new-p");
    }

    // 鈹€鈹€ Delete 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void Delete_ExistingSession_ReturnsTrueAndRemoves()
    {
        var created = _svc.CreateSession("T", "p1");

        bool result = _repo.Delete(created.Id);

        result.Should().BeTrue();
        _repo.Get(created.Id).Should().BeNull();
    }

    [Fact]
    public void Delete_NonExistentSession_ReturnsFalse()
    {
        _repo.Delete("no-such-id").Should().BeFalse();
    }

    // 鈹€鈹€ GetRootSessionId 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void GetRootSessionId_ForTopLevelSession_ReturnsSelf()
    {
        var created = _svc.CreateSession("Root", "p1");

        _repo.GetRootSessionId(created.Id).Should().Be(created.Id);
    }

    [Fact]
    public void GetRootSessionId_ForTwoLevelNesting_ReturnsRoot()
    {
        var root = _svc.CreateSession("Root", "p1");
        var child = _svc.CreateSession("Child", "p1", parentSessionId: root.Id);

        string rootId = _repo.GetRootSessionId(child.Id);

        rootId.Should().Be(root.Id);
    }

    [Fact]
    public void GetRootSessionId_ForThreeLevelNesting_ReturnsRoot()
    {
        var root = _svc.CreateSession("Root", "p1");
        var child = _svc.CreateSession("Child", "p1", parentSessionId: root.Id);
        var grandchild = _svc.CreateSession("Grandchild", "p1", parentSessionId: child.Id);

        string rootId = _repo.GetRootSessionId(grandchild.Id);

        rootId.Should().Be(root.Id);
    }

    // 鈹€鈹€ FindIdleSubAgentSession 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void FindIdleSubAgentSession_WithMatchingIdle_ReturnsIt()
    {
        var parent = _svc.CreateSession("Parent", "p1");
        var sub = _svc.CreateSession("Sub", "p1", agentId: "agent-x", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-x", activeSessionIds: []);

        found.Should().NotBeNull();
        found!.Id.Should().Be(sub.Id);
    }

    [Fact]
    public void FindIdleSubAgentSession_WhenActive_ReturnsNull()
    {
        var parent = _svc.CreateSession("Parent", "p1");
        var sub = _svc.CreateSession("Sub", "p1", agentId: "agent-y", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-y",
            activeSessionIds: [sub.Id]);

        found.Should().BeNull();
    }

    [Fact]
    public void FindIdleSubAgentSession_WrongAgentId_ReturnsNull()
    {
        var parent = _svc.CreateSession("Parent", "p1");
        _svc.CreateSession("Sub", "p1", agentId: "agent-A", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-B", activeSessionIds: []);

        found.Should().BeNull();
    }

    // 鈹€鈹€ Messages 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void AddMessage_ThenGetMessages_ReturnsAddedMessage()
    {
        var created = _svc.CreateSession("T", "p1");
        var message = new SessionMessage(
            Id: "msg1", Role: "user", Content: "Hello",
            ThinkContent: null, Timestamp: DateTimeOffset.UtcNow, Attachments: null);

        _repo.AddMessage(created.Id, message);
        var messages = _repo.GetMessages(created.Id);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be("msg1");
        messages[0].Content.Should().Be("Hello");
    }

    [Fact]
    public void RemoveMessages_RemovesSpecifiedIds()
    {
        var created = _svc.CreateSession("T", "p1");
        var m1 = new SessionMessage("id1", "user", "A", null, DateTimeOffset.UtcNow, null);
        var m2 = new SessionMessage("id2", "user", "B", null, DateTimeOffset.UtcNow, null);
        var m3 = new SessionMessage("id3", "user", "C", null, DateTimeOffset.UtcNow, null);
        _repo.AddMessage(created.Id, m1);
        _repo.AddMessage(created.Id, m2);
        _repo.AddMessage(created.Id, m3);

        _repo.RemoveMessages(created.Id, new HashSet<string> { "id1", "id3" });

        var remaining = _repo.GetMessages(created.Id);
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be("id2");
    }
}


