using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
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

        AgentStore agentStore = new();
        WebChannel webChannel = new(hubContext);
        PetStateStore petStateStore = new(_tempDir.Path);
        EmotionStore emotionStore = new(_tempDir.Path);
        PetContextFactory contextFactory = new(petStateStore, emotionStore);
        PetFactory petFactory = new(petStateStore, contextFactory, _tempDir.Path, Microsoft.Extensions.Logging.Abstractions.NullLogger<PetFactory>.Instance);
        _svc = new SessionService(agentStore, hubContext, [webChannel], petFactory, _tempDir.Path);
        _repo = _svc;
    }

    public void Dispose() => _tempDir.Dispose();

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
    public void CreateSession_WithAgentId_PreservesAgentId()
    {
        var session = _svc.CreateSession("Sub Task", "p1", agentId: "agent-123");

        session.AgentId.Should().Be("agent-123");
        _repo.Get(session.Id)!.AgentId.Should().Be("agent-123");
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
