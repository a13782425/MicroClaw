using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// SessionStore 实现 ISessionRepository 的集成测试（O-1-11）。
/// 每个测试使用独立的临时目录，文件系统完全隔离。
/// 不调用 <see cref="TestConfigFixture.EnsureInitialized"/>，避免重置全局静态 MicroClawConfig
/// 污染并发运行的其他测试（与 SessionStoreTests 保持相同模式）。
/// </summary>
public sealed class SessionRepositoryTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionStore _store;
    private readonly ISessionRepository _repo;

    public SessionRepositoryTests()
    {
        _store = new SessionStore(_tempDir.Path);
        _repo = (ISessionRepository)_store;
    }

    public void Dispose() => _tempDir.Dispose();

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_AfterCreate_ReturnsDomainObject()
    {
        var info = _store.Create("Test", "p1");

        Session? session = _repo.Get(info.Id);

        session.Should().NotBeNull();
        session!.Id.Should().Be(info.Id);
        session.Title.Should().Be("Test");
        session.ProviderId.Should().Be("p1");
        session.IsApproved.Should().BeFalse();
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        _repo.Get("does-not-exist").Should().BeNull();
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsAllSessionsAsDomainObjects()
    {
        _store.Create("A", "p1");
        _store.Create("B", "p1");

        var all = _repo.GetAll();

        all.Should().HaveCount(2);
        all.Should().AllBeOfType<Session>();
    }

    // ── GetTopLevel ───────────────────────────────────────────────────────────

    [Fact]
    public void GetTopLevel_ExcludesSubAgentSessions()
    {
        _store.Create("Root", "p1");
        _store.Create("Sub", "p1", parentSessionId: "some-parent-id");

        var topLevel = _repo.GetTopLevel();

        topLevel.Should().HaveCount(1);
        topLevel[0].Title.Should().Be("Root");
    }

    // ── Save (update existing) ────────────────────────────────────────────────

    [Fact]
    public void Save_ExistingSession_PersistsChangedFields()
    {
        var info = _store.Create("Original", "p1");
        Session session = _repo.Get(info.Id)!;

        session.Approve("reason");
        _repo.Save(session);

        Session? reloaded = _repo.Get(info.Id);
        reloaded.Should().NotBeNull();
        reloaded!.IsApproved.Should().BeTrue();
        reloaded.ApprovalReason.Should().Be("reason");
    }

    [Fact]
    public void Save_ExistingSession_UpdatedProvider_IsPersisted()
    {
        var info = _store.Create("T", "old-p");
        Session session = _repo.Get(info.Id)!;

        session.UpdateProvider("new-p");
        session.PopDomainEvents(); // 清空，避免影响断言
        _repo.Save(session);

        Session? reloaded = _repo.Get(info.Id);
        reloaded!.ProviderId.Should().Be("new-p");
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingSession_ReturnsTrueAndRemoves()
    {
        var info = _store.Create("T", "p1");

        bool result = _repo.Delete(info.Id);

        result.Should().BeTrue();
        _repo.Get(info.Id).Should().BeNull();
    }

    [Fact]
    public void Delete_NonExistentSession_ReturnsFalse()
    {
        _repo.Delete("no-such-id").Should().BeFalse();
    }

    // ── GetRootSessionId ──────────────────────────────────────────────────────

    [Fact]
    public void GetRootSessionId_ForTopLevelSession_ReturnsSelf()
    {
        var info = _store.Create("Root", "p1");

        _repo.GetRootSessionId(info.Id).Should().Be(info.Id);
    }

    [Fact]
    public void GetRootSessionId_ForTwoLevelNesting_ReturnsRoot()
    {
        var root = _store.Create("Root", "p1");
        var child = _store.Create("Child", "p1", parentSessionId: root.Id);

        string rootId = _repo.GetRootSessionId(child.Id);

        rootId.Should().Be(root.Id);
    }

    [Fact]
    public void GetRootSessionId_ForThreeLevelNesting_ReturnsRoot()
    {
        var root = _store.Create("Root", "p1");
        var child = _store.Create("Child", "p1", parentSessionId: root.Id);
        var grandchild = _store.Create("Grandchild", "p1", parentSessionId: child.Id);

        string rootId = _repo.GetRootSessionId(grandchild.Id);

        rootId.Should().Be(root.Id);
    }

    // ── FindIdleSubAgentSession ───────────────────────────────────────────────

    [Fact]
    public void FindIdleSubAgentSession_WithMatchingIdle_ReturnsIt()
    {
        var parent = _store.Create("Parent", "p1");
        var sub = _store.Create("Sub", "p1", agentId: "agent-x", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-x", activeSessionIds: []);

        found.Should().NotBeNull();
        found!.Id.Should().Be(sub.Id);
    }

    [Fact]
    public void FindIdleSubAgentSession_WhenActive_ReturnsNull()
    {
        var parent = _store.Create("Parent", "p1");
        var sub = _store.Create("Sub", "p1", agentId: "agent-y", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-y",
            activeSessionIds: [sub.Id]);

        found.Should().BeNull();
    }

    [Fact]
    public void FindIdleSubAgentSession_WrongAgentId_ReturnsNull()
    {
        var parent = _store.Create("Parent", "p1");
        _store.Create("Sub", "p1", agentId: "agent-A", parentSessionId: parent.Id);

        Session? found = _repo.FindIdleSubAgentSession(parent.Id, "agent-B", activeSessionIds: []);

        found.Should().BeNull();
    }

    // ── Messages (via ISessionRepository) ────────────────────────────────────

    [Fact]
    public void AddMessage_ThenGetMessages_ReturnsAddedMessage()
    {
        var info = _store.Create("T", "p1");
        var message = new SessionMessage(
            Id: "msg1", Role: "user", Content: "Hello",
            ThinkContent: null, Timestamp: DateTimeOffset.UtcNow, Attachments: null);

        _repo.AddMessage(info.Id, message);
        var messages = _repo.GetMessages(info.Id);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be("msg1");
        messages[0].Content.Should().Be("Hello");
    }

    [Fact]
    public void RemoveMessages_RemovesSpecifiedIds()
    {
        var info = _store.Create("T", "p1");
        var m1 = new SessionMessage("id1", "user", "A", null, DateTimeOffset.UtcNow, null);
        var m2 = new SessionMessage("id2", "user", "B", null, DateTimeOffset.UtcNow, null);
        var m3 = new SessionMessage("id3", "user", "C", null, DateTimeOffset.UtcNow, null);
        _repo.AddMessage(info.Id, m1);
        _repo.AddMessage(info.Id, m2);
        _repo.AddMessage(info.Id, m3);

        _repo.RemoveMessages(info.Id, new HashSet<string> { "id1", "id3" });

        var remaining = _repo.GetMessages(info.Id);
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be("id2");
    }
}
