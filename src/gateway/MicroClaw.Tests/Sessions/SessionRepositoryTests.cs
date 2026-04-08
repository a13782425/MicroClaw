using FluentAssertions;
using MicroClaw.Agent;
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
    public void Get_AfterCreate_ReturnsDomainObject()
    {
        var session = _svc.CreateSession("Test", "p1");

        Session? result = _repo.Get(session.Id) as Session;

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

    [Fact]
    public void GetAll_ReturnsAllSessionsAsDomainObjects()
    {
        _svc.CreateSession("A", "p1");
        _svc.CreateSession("B", "p1");

        var all = _repo.GetAll();

        all.Should().HaveCount(2);
        all.Should().AllBeOfType<Session>();
    }

    [Fact]
    public void Save_ExistingSession_PersistsChangedFields()
    {
        var created = _svc.CreateSession("Original", "p1");
        Session microSession = (_repo.Get(created.Id) as Session)!;

        microSession.Approve("reason");
        _repo.Save(microSession);

        Session? reloaded = _repo.Get(created.Id) as Session;
        reloaded.Should().NotBeNull();
        reloaded!.IsApproved.Should().BeTrue();
        reloaded.ApprovalReason.Should().Be("reason");
    }

    [Fact]
    public void Save_ExistingSession_UpdatedProvider_IsPersisted()
    {
        var created = _svc.CreateSession("T", "old-p");
        Session microSession = (_repo.Get(created.Id) as Session)!;

        microSession.UpdateProvider("new-p");
        microSession.PopDomainEvents();
        _repo.Save(microSession);

        Session? reloaded = _repo.Get(created.Id) as Session;
        reloaded!.ProviderId.Should().Be("new-p");
    }

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
