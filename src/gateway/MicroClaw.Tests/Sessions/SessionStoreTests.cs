using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Sessions;

public sealed class SessionStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionStore _store;

    public SessionStoreTests()
    {
        _store = new SessionStore(_tempDir.Path);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    // --- Create / Get / All ---

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Create_ReturnsSessionWithGeneratedId()
    {
        var session = _store.Create("Test Session", "provider-1");

        session.Id.Should().NotBeNullOrWhiteSpace();
        session.Title.Should().Be("Test Session");
        session.ProviderId.Should().Be("provider-1");
        session.IsApproved.Should().BeFalse();
        session.ChannelType.Should().Be(ChannelType.Web);
        session.ChannelId.Should().Be("web");
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithCustomId_UsesProvidedId()
    {
        var session = _store.Create("Test", "p1", id: "custom-id-123");

        session.Id.Should().Be("custom-id-123");
    }

    [Fact]
    public void Create_WithChannelType_PreservesType()
    {
        var session = _store.Create("Feishu Session", "p1", ChannelType.Feishu);

        session.ChannelType.Should().Be(ChannelType.Feishu);
    }

    [Fact]
    public void Get_ExistingSession_ReturnsIt()
    {
        var created = _store.Create("Test", "p1");

        var result = _store.Get(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Test");
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        _store.Get("non-existent").Should().BeNull();
    }

    [Fact]
    public void All_ReturnsAllSessions_OrderedByCreatedAtDesc()
    {
        _store.Create("First", "p1");
        _store.Create("Second", "p1");
        _store.Create("Third", "p1");

        var all = _store.All;

        all.Should().HaveCount(3);
        all[0].Title.Should().Be("Third");
        all[2].Title.Should().Be("First");
    }

    // --- Approve / Disable ---

    [Fact]
    public void Approve_ExistingSession_SetsIsApprovedTrue()
    {
        var session = _store.Create("Test", "p1");

        var result = _store.Approve(session.Id);

        result.Should().NotBeNull();
        result!.IsApproved.Should().BeTrue();
    }

    [Fact]
    public void Approve_NonExistent_ReturnsNull()
    {
        _store.Approve("non-existent").Should().BeNull();
    }

    [Fact]
    public void Disable_ApprovedSession_SetsIsApprovedFalse()
    {
        var session = _store.Create("Test", "p1");
        _store.Approve(session.Id);

        var result = _store.Disable(session.Id);

        result.Should().NotBeNull();
        result!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public void Disable_NonExistent_ReturnsNull()
    {
        _store.Disable("non-existent").Should().BeNull();
    }

    // --- Delete ---

    [Fact]
    public void Delete_ExistingSession_ReturnsTrueAndRemoves()
    {
        var session = _store.Create("Test", "p1");

        _store.Delete(session.Id).Should().BeTrue();
        _store.Get(session.Id).Should().BeNull();
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _store.Delete("non-existent").Should().BeFalse();
    }

    [Fact]
    public void Delete_CleansUpSessionDirectory()
    {
        var session = _store.Create("Test", "p1");
        _store.AddMessage(session.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "hello", null, DateTimeOffset.UtcNow, null));

        string sessionDir = Path.Combine(_tempDir.Path, session.Id);
        Directory.Exists(sessionDir).Should().BeTrue();

        _store.Delete(session.Id);

        Directory.Exists(sessionDir).Should().BeFalse();
    }

    // --- Messages ---

    [Fact]
    public void GetMessages_NoMessages_ReturnsEmptyList()
    {
        var session = _store.Create("Test", "p1");

        _store.GetMessages(session.Id).Should().BeEmpty();
    }

    [Fact]
    public void AddMessage_ThenGetMessages_ReturnsMessages()
    {
        var session = _store.Create("Test", "p1");
        var timestamp = DateTimeOffset.UtcNow;

        _store.AddMessage(session.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "Hello", null, timestamp, null));
        _store.AddMessage(session.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "assistant", "Hi there!", "Thinking...", timestamp.AddSeconds(1), null));

        var messages = _store.GetMessages(session.Id);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("Hello");
        messages[0].ThinkContent.Should().BeNull();
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("Hi there!");
        messages[1].ThinkContent.Should().Be("Thinking...");
    }

    [Fact]
    public void AddMessage_WithAttachments_PreservesAttachments()
    {
        var session = _store.Create("Test", "p1");
        var attachments = new List<MessageAttachment>
        {
            new("test.png", "image/png", "iVBORw0KGgo=")
        }.AsReadOnly();

        _store.AddMessage(session.Id,
            new SessionMessage(Guid.NewGuid().ToString("N"), "user", "See image", null, DateTimeOffset.UtcNow, attachments));

        var messages = _store.GetMessages(session.Id);

        messages.Should().ContainSingle();
        messages[0].Attachments.Should().ContainSingle();
        messages[0].Attachments![0].FileName.Should().Be("test.png");
        messages[0].Attachments![0].MimeType.Should().Be("image/png");
        messages[0].Attachments![0].Base64Data.Should().Be("iVBORw0KGgo=");
    }

    [Fact]
    public void AddMessage_CreatesSessionDirectoryAutomatically()
    {
        string customId = "test-session-dir-creation";
        _store.Create("Test", "p1", id: customId);

        string sessionDir = Path.Combine(_tempDir.Path, customId);

        _store.AddMessage(customId, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "hi", null, DateTimeOffset.UtcNow, null));

        Directory.Exists(sessionDir).Should().BeTrue();
        File.Exists(Path.Combine(sessionDir, "messages.jsonl")).Should().BeTrue();
    }

    // --- Sub-Agent 字段测试 ---

    [Fact]
    public void Create_WithoutAgentId_AgentIdIsNull()
    {
        var session = _store.Create("Normal Session", "p1");

        session.AgentId.Should().BeNull();
        _store.Get(session.Id)!.AgentId.Should().BeNull();
    }

    [Fact]
    public void Create_WithAgentId_PreservesAgentId()
    {
        var session = _store.Create("Sub Task", "p1", agentId: "agent-123");

        session.AgentId.Should().Be("agent-123");
        _store.Get(session.Id)!.AgentId.Should().Be("agent-123");
    }

    [Fact]
    public void Create_WithParentSessionId_PreservesParentSessionId()
    {
        var parent = _store.Create("Parent", "p1");
        var child = _store.Create("Child", "p1", parentSessionId: parent.Id);

        child.ParentSessionId.Should().Be(parent.Id);
        _store.Get(child.Id)!.ParentSessionId.Should().Be(parent.Id);
    }

    [Fact]
    public void Create_SubAgentSession_BothFieldsPersisted()
    {
        var parent = _store.Create("Parent Task", "p1");
        var child = _store.Create("[子代理] helper", "p1",
            agentId: "agent-456",
            parentSessionId: parent.Id);

        var retrieved = _store.Get(child.Id)!;
        retrieved.AgentId.Should().Be("agent-456");
        retrieved.ParentSessionId.Should().Be(parent.Id);
    }

    // --- ChannelId 绑定测试 ---

    [Fact]
    public void Create_DefaultChannelId_IsWeb()
    {
        var session = _store.Create("Test", "p1");

        session.ChannelId.Should().Be("web");
        _store.Get(session.Id)!.ChannelId.Should().Be("web");
    }

    [Fact]
    public void Create_WithCustomChannelId_PreservesChannelId()
    {
        string channelId = "feishu-channel-abc123";
        var session = _store.Create("Feishu Session", "p1", channelId: channelId);

        session.ChannelId.Should().Be(channelId);
        _store.Get(session.Id)!.ChannelId.Should().Be(channelId);
    }

    [Fact]
    public void Create_WithAgentId_ChannelIdAndAgentIdBothPersisted()
    {
        var session = _store.Create("Bound Session", "p1",
            channelId: "my-channel-id",
            agentId: "my-agent-id");

        var retrieved = _store.Get(session.Id)!;
        retrieved.ChannelId.Should().Be("my-channel-id");
        retrieved.AgentId.Should().Be("my-agent-id");
    }
}
