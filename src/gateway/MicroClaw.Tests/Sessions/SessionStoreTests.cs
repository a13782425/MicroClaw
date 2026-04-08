using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Configuration.Options;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// SessionService �ĳ־û��ͻ��� CRUD ���ԣ����ԭ SessionStoreTests����
/// </summary>
public sealed class SessionServiceStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionService _svc;
    private readonly ISessionRepository _repo;

    public SessionServiceStoreTests()
    {
        TestConfigFixture.EnsureInitialized();

        var hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var clients = Substitute.For<IHubClients>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(Substitute.For<IClientProxy>());

        AgentStore agentStore = new AgentStore();
        WebChannel webChannel = new(hubContext);
        PetStateStore petStateStore = new(_tempDir.Path);
        EmotionStore emotionStore = new(_tempDir.Path);
        PetContextFactory contextFactory = new(petStateStore, emotionStore);
        PetFactory petFactory = new(petStateStore, contextFactory, _tempDir.Path, Microsoft.Extensions.Logging.Abstractions.NullLogger<PetFactory>.Instance);
        _svc = new SessionService(agentStore, hubContext, [webChannel], petFactory, _tempDir.Path);
        _repo = _svc;
    }

    public void Dispose() => _tempDir.Dispose();

    // --- Create / Get ---

    [Fact]
    public void CreateSession_ReturnsSessionWithGeneratedId()
    {
        var session = _svc.CreateSession("Test Session", "provider-1");

        session.Id.Should().NotBeNullOrWhiteSpace();
        session.Title.Should().Be("Test Session");
        session.ProviderId.Should().Be("provider-1");
        session.IsApproved.Should().BeFalse();
        session.ChannelType.Should().Be(ChannelType.Web);
        session.ChannelId.Should().Be("web");
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateSession_WithCustomId_UsesProvidedId()
    {
        var session = _svc.CreateSession("Test", "p1", id: "custom-id-123");

        session.Id.Should().Be("custom-id-123");
    }

    [Fact]
    public void CreateSession_WithChannelType_PreservesType()
    {
        var session = _svc.CreateSession("Feishu Session", "p1", ChannelType.Feishu);

        session.ChannelType.Should().Be(ChannelType.Feishu);
    }

    [Fact]
    public void Get_ExistingSession_ReturnsIt()
    {
        var created = _svc.CreateSession("Test", "p1");

        Session? result = _repo.Get(created.Id) as Session;

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Test");
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        _repo.Get("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllSessions_OrderedByCreatedAtDesc()
    {
        _svc.CreateSession("First", "p1");
        _svc.CreateSession("Second", "p1");
        _svc.CreateSession("Third", "p1");

        var all = _repo.GetAll();

        all.Should().HaveCount(3);
        all[0].Title.Should().Be("Third");
        all[2].Title.Should().Be("First");
    }

    [Fact]
    public void GetTopLevel_WhenEmpty_ReturnsEmptyList()
    {
        _repo.GetTopLevel().Should().BeEmpty();
    }

    // --- Approve / Disable (via domain object + Save) ---

    [Fact]
    public void Approve_ExistingSession_SetsIsApprovedTrue()
    {
        var created = _svc.CreateSession("Test", "p1");
        Session microSession = (_repo.Get(created.Id) as Session)!;

        microSession.Approve();
        _repo.Save(microSession);

        _repo.Get(created.Id)!.IsApproved.Should().BeTrue();
    }

    [Fact]
    public void Disable_ApprovedSession_SetsIsApprovedFalse()
    {
        var created = _svc.CreateSession("Test", "p1");
        Session microSession = (_repo.Get(created.Id) as Session)!;
        microSession.Approve();
        _repo.Save(microSession);

        microSession.Disable();
        _repo.Save(microSession);

        _repo.Get(created.Id)!.IsApproved.Should().BeFalse();
    }

    // --- Delete ---

    [Fact]
    public void Delete_ExistingSession_ReturnsTrueAndRemoves()
    {
        var created = _svc.CreateSession("Test", "p1");

        _repo.Delete(created.Id).Should().BeTrue();
        _repo.Get(created.Id).Should().BeNull();
        _repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _repo.Delete("non-existent").Should().BeFalse();
    }

    [Fact]
    public void Delete_CleansUpSessionDirectory()
    {
        var created = _svc.CreateSession("Test", "p1");
        _repo.AddMessage(created.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "hello", null, DateTimeOffset.UtcNow, null));

        string sessionDir = Path.Combine(_tempDir.Path, created.Id);
        Directory.Exists(sessionDir).Should().BeTrue();

        _repo.Delete(created.Id);

        Directory.Exists(sessionDir).Should().BeFalse();
    }

    // --- Messages ---

    [Fact]
    public void GetMessages_NoMessages_ReturnsEmptyList()
    {
        var created = _svc.CreateSession("Test", "p1");

        _repo.GetMessages(created.Id).Should().BeEmpty();
    }

    [Fact]
    public void AddMessage_ThenGetMessages_ReturnsMessages()
    {
        var created = _svc.CreateSession("Test", "p1");
        var timestamp = DateTimeOffset.UtcNow;

        _repo.AddMessage(created.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "Hello", null, timestamp, null));
        _repo.AddMessage(created.Id, new SessionMessage(Guid.NewGuid().ToString("N"), "assistant", "Hi there!", "Thinking...", timestamp.AddSeconds(1), null));

        var messages = _repo.GetMessages(created.Id);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("Hello");
        messages[1].Role.Should().Be("assistant");
        messages[1].ThinkContent.Should().Be("Thinking...");
    }

    [Fact]
    public void AddMessage_WithAttachments_PreservesAttachments()
    {
        var created = _svc.CreateSession("Test", "p1");
        var attachments = new List<MessageAttachment>
        {
            new("test.png", "image/png", "iVBORw0KGgo=")
        }.AsReadOnly();

        _repo.AddMessage(created.Id,
            new SessionMessage(Guid.NewGuid().ToString("N"), "user", "See image", null, DateTimeOffset.UtcNow, attachments));

        var messages = _repo.GetMessages(created.Id);

        messages.Should().ContainSingle();
        messages[0].Attachments![0].FileName.Should().Be("test.png");
    }

    [Fact]
    public void AddMessage_CreatesSessionDirectoryAutomatically()
    {
        string customId = "test-dir-creation-svc";
        _svc.CreateSession("Test", "p1", id: customId);

        _repo.AddMessage(customId, new SessionMessage(Guid.NewGuid().ToString("N"), "user", "hi", null, DateTimeOffset.UtcNow, null));

        Directory.Exists(Path.Combine(_tempDir.Path, customId)).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir.Path, customId, "messages.jsonl")).Should().BeTrue();
    }

    // --- Sub-Agent �ֶ� ---

    [Fact]
    public void CreateSession_WithoutAgentId_AgentIdIsNull()
    {
        var session = _svc.CreateSession("Normal Session", "p1");

        session.AgentId.Should().BeNull();
        _repo.Get(session.Id)!.AgentId.Should().BeNull();
    }

    [Fact]
    public void CreateSession_WithAgentId_PreservesAgentId()
    {
        var session = _svc.CreateSession("Sub Task", "p1", agentId: "agent-123");

        session.AgentId.Should().Be("agent-123");
        _repo.Get(session.Id)!.AgentId.Should().Be("agent-123");
    }

    [Fact]
    public void CreateSession_WithParentSessionId_PreservesParentSessionId()
    {
        var parent = _svc.CreateSession("Parent", "p1");
        var child = _svc.CreateSession("Child", "p1", parentSessionId: parent.Id);

        child.ParentSessionId.Should().Be(parent.Id);
        _repo.Get(child.Id)!.ParentSessionId.Should().Be(parent.Id);
    }

    // --- ChannelId �� ---

    [Fact]
    public void CreateSession_DefaultChannelId_IsWeb()
    {
        var session = _svc.CreateSession("Test", "p1");

        session.ChannelId.Should().Be("web");
        _repo.Get(session.Id)!.ChannelId.Should().Be("web");
    }

    [Fact]
    public void CreateSession_WithCustomChannelId_PreservesChannelId()
    {
        string channelId = "feishu-channel-abc123";
        var session = _svc.CreateSession("Feishu Session", "p1", channelId: channelId);

        session.ChannelId.Should().Be(channelId);
        _repo.Get(session.Id)!.ChannelId.Should().Be(channelId);
    }
}
