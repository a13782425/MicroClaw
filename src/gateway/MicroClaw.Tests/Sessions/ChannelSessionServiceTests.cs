using FluentAssertions;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

public sealed class ChannelSessionServiceTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionStore _sessionStore;
    private readonly IHubContext<GatewayHub> _hubContext;
    private readonly ChannelSessionService _service;

    public ChannelSessionServiceTests()
    {
        _sessionStore = new SessionStore(_db.CreateFactory(), _tempDir.Path);

        _hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        _hubContext.Clients.Returns(clients);
        clients.All.Returns(clientProxy);

        _service = new ChannelSessionService(_sessionStore, _hubContext);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
        _db.Dispose();
    }

    // --- Deterministic Session ID ---

    [Fact]
    public void GenerateSessionId_IsDeterministic()
    {
        string id1 = ChannelSessionService.GenerateSessionId(ChannelType.Feishu, "channel-1", "sender-1");
        string id2 = ChannelSessionService.GenerateSessionId(ChannelType.Feishu, "channel-1", "sender-1");

        id1.Should().Be(id2);
    }

    [Fact]
    public void GenerateSessionId_DifferentInputs_ProduceDifferentIds()
    {
        string id1 = ChannelSessionService.GenerateSessionId(ChannelType.Feishu, "channel-1", "sender-1");
        string id2 = ChannelSessionService.GenerateSessionId(ChannelType.Feishu, "channel-1", "sender-2");
        string id3 = ChannelSessionService.GenerateSessionId(ChannelType.WeCom, "channel-1", "sender-1");

        id1.Should().NotBe(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void GenerateSessionId_Returns32HexChars()
    {
        string id = ChannelSessionService.GenerateSessionId(ChannelType.Feishu, "ch", "sender");

        id.Should().HaveLength(32);
        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    // --- FindOrCreateSession ---

    [Fact]
    public void FindOrCreateSession_CreatesNewSession()
    {
        var session = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-abc123456", "My Feishu Bot", "provider-1");

        session.Should().NotBeNull();
        session.ChannelType.Should().Be(ChannelType.Feishu);
        session.ProviderId.Should().Be("provider-1");
        session.Title.Should().Contain("My Feishu Bot");
        session.Title.Should().Contain("sender-a");
        session.IsApproved.Should().BeFalse();
    }

    [Fact]
    public void FindOrCreateSession_ReturnsSameSessionOnSecondCall()
    {
        var session1 = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-1", "Bot", "provider-1");
        var session2 = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-1", "Bot", "provider-1");

        session1.Id.Should().Be(session2.Id);
    }

    [Fact]
    public void FindOrCreateSession_DifferentSenders_CreateDifferentSessions()
    {
        var session1 = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-1", "Bot", "provider-1");
        var session2 = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-2", "Bot", "provider-1");

        session1.Id.Should().NotBe(session2.Id);
    }

    // --- AddMessage / GetMessages delegation ---

    [Fact]
    public void AddMessage_DelegatesToSessionStore()
    {
        var session = _service.FindOrCreateSession(
            ChannelType.Feishu, "ch", "sender", "Bot", "p1");

        var msg = new SessionMessage("user", "Hello", null, DateTimeOffset.UtcNow, null);
        _service.AddMessage(session.Id, msg);

        var messages = _service.GetMessages(session.Id);
        messages.Should().ContainSingle()
            .Which.Content.Should().Be("Hello");
    }

    // --- NotifyPendingApprovalAsync ---

    [Fact]
    public async Task NotifyPendingApprovalAsync_SendsSignalRMessage()
    {
        await _service.NotifyPendingApprovalAsync("session-1", "Test Session", ChannelType.Feishu);

        await _hubContext.Clients.All.Received(1).SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPendingApprovalAsync_Throttles_WithinFiveMinutes()
    {
        await _service.NotifyPendingApprovalAsync("throttle-test", "Test", ChannelType.Feishu);
        await _service.NotifyPendingApprovalAsync("throttle-test", "Test", ChannelType.Feishu);

        await _hubContext.Clients.All.Received(1).SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPendingApprovalAsync_DifferentSessions_NotThrottled()
    {
        await _service.NotifyPendingApprovalAsync("session-a", "Test A", ChannelType.Feishu);
        await _service.NotifyPendingApprovalAsync("session-b", "Test B", ChannelType.Feishu);

        await _hubContext.Clients.All.Received(2).SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    // --- CheckApprovalAsync ---

    [Fact]
    public async Task CheckApprovalAsync_ApprovedSession_ReturnsTrueWithoutNotify()
    {
        // 创建会话后手动审批
        var session = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-1", "sender-approved", "Bot", "provider-1");
        _sessionStore.Approve(session.Id);
        var approved = _sessionStore.Get(session.Id)!;

        bool result = await _service.CheckApprovalAsync(approved, ChannelType.Feishu);

        result.Should().BeTrue();
        await _hubContext.Clients.All.DidNotReceive().SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckApprovalAsync_PendingSession_ReturnsFalseAndNotifiesAdmin()
    {
        var session = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-2", "sender-pending", "Bot", "provider-1");

        bool result = await _service.CheckApprovalAsync(session, ChannelType.Feishu);

        result.Should().BeFalse();
        await _hubContext.Clients.All.Received(1).SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckApprovalAsync_PendingSession_NotificationIsThrottled()
    {
        var session = _service.FindOrCreateSession(
            ChannelType.Feishu, "channel-3", "sender-throttle", "Bot", "provider-1");

        // 连续两次调用，第二次应被限流
        await _service.CheckApprovalAsync(session, ChannelType.Feishu);
        await _service.CheckApprovalAsync(session, ChannelType.Feishu);

        await _hubContext.Clients.All.Received(1).SendCoreAsync(
            "sessionPendingApproval",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }
}
