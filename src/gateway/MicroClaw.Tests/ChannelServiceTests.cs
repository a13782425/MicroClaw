using System.Reflection;
using FluentAssertions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MicroClaw.Tests;

public sealed class ChannelServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetRequired_SameChannelId_ReturnsCachedInstance()
    {
        InitializeConfig(
            channels:
            [
                new ChannelEntity
                {
                    Id = "feishu-a",
                    DisplayName = "Feishu A",
                    ChannelType = ChannelType.Feishu,
                    IsEnabled = true,
                    SettingJson = """{"appId":"a"}"""
                }
            ]);


    }

    [Fact]
    public void GetRequired_DifferentChannelConfigs_ReturnDistinctInstances()
    {
        InitializeConfig(
            channels:
            [
                new ChannelEntity
                {
                    Id = "feishu-a",
                    DisplayName = "Feishu A",
                    ChannelType = ChannelType.Feishu,
                    IsEnabled = true,
                    SettingJson = """{"appId":"a"}"""
                },
                new ChannelEntity
                {
                    Id = "feishu-b",
                    DisplayName = "Feishu B",
                    ChannelType = ChannelType.Feishu,
                    IsEnabled = true,
                    SettingJson = """{"appId":"b"}"""
                }
            ]);


    }

    [Fact]
    public async Task SessionService_CreateSession_DoesNotAttachChannelAutomatically()
    {
        InitializeConfig(
            channels:
            [
                new ChannelEntity
                {
                    Id = ChannelService.WebChannelId,
                    DisplayName = "Web Console",
                    ChannelType = ChannelType.Web,
                    IsEnabled = true,
                    SettingJson = "{}"
                }
            ]);

        var (_, _, service) = CreateSessionService();
        await StartServiceAsync(service);

    }

    [Fact]
    public async Task SessionService_InitializeAsync_WarmsSessionsIntoCache()
    {
        InitializeConfig(
            channels: [],
            sessions:
            [
                new SessionEntity
                {
                    Id = "session-a",
                    Title = "Session A",
                    ProviderId = "provider-a",
                    IsApproved = true,
                    ChannelType = "web",
                    ChannelId = "web",
                    CreatedAtMs = 1000,
                    AgentId = "agent-a",
                    ApprovalReason = "ok"
                },
                new SessionEntity
                {
                    Id = "session-b",
                    Title = "Session B",
                    ProviderId = "provider-b",
                    IsApproved = false,
                    ChannelType = "web",
                    ChannelId = "web",
                    CreatedAtMs = 2000
                }
            ]);

        var (_, petFactory, service) = CreateSessionService();

        await StartServiceAsync(service);

        Session first = (Session)((MicroClaw.Abstractions.Sessions.ISessionService)service).Get("session-a")!;
        Session second = (Session)((MicroClaw.Abstractions.Sessions.ISessionService)service).Get("session-a")!;

        ReferenceEquals(first, second).Should().BeTrue();
        ((MicroClaw.Abstractions.Sessions.ISessionService)service).GetAll().Should().HaveCount(2);
        await petFactory.Received(2).CreateOrLoadAsync(Arg.Any<MicroClaw.Abstractions.Sessions.IMicroSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionService_Save_PersistsUpdatedCachedSession()
    {
        InitializeConfig(
            channels: [],
            sessions:
            [
                new SessionEntity
                {
                    Id = "session-a",
                    Title = "Old Title",
                    ProviderId = "provider-a",
                    IsApproved = false,
                    ChannelType = "web",
                    ChannelId = "web",
                    CreatedAtMs = 1000
                }
            ]);

        var (_, _, service) = CreateSessionService();
        await StartServiceAsync(service);

        var repo = (MicroClaw.Abstractions.Sessions.ISessionService)service;
        Session session = (Session)repo.Get("session-a")!;
        session.UpdateTitle("New Title");
        repo.Save(session);

        MicroClawConfig.Get<SessionsOptions>().Items.Should().ContainSingle(x => x.Id == "session-a" && x.Title == "New Title");
        session.Entity.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task SessionService_Delete_RemovesSessionFromCacheAndConfig()
    {
        InitializeConfig(
            channels: [],
            sessions:
            [
                new SessionEntity
                {
                    Id = "session-a",
                    Title = "Session A",
                    ProviderId = "provider-a",
                    IsApproved = false,
                    ChannelType = "web",
                    ChannelId = "web",
                    CreatedAtMs = 1000
                }
            ]);

        string sessionDir = Path.Combine(_tempRoot, "workspace", "sessions", "session-a");
        Directory.CreateDirectory(sessionDir);

        var (_, _, service) = CreateSessionService();
        await StartServiceAsync(service);

        var repo = (MicroClaw.Abstractions.Sessions.ISessionService)service;
        repo.Delete("session-a").Should().BeTrue();
        repo.Get("session-a").Should().BeNull();
        MicroClawConfig.Get<SessionsOptions>().Items.Should().BeEmpty();
        Directory.Exists(sessionDir).Should().BeFalse();
    }

    public void Dispose()
    {
        ResetMicroClawConfig();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private (IHubContext<GatewayHub> HubContext, IPetFactory PetFactory, MicroClaw.Sessions.SessionService Service) CreateSessionService()
    {
        var hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var petFactory = Substitute.For<IPetFactory>();
        petFactory.CreateOrLoadAsync(Arg.Any<MicroClaw.Abstractions.Sessions.IMicroSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IPet?>(null));

        var agentStore = new MicroClaw.Agent.AgentStore();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(MicroClaw.Agent.AgentStore)).Returns(agentStore);
        sp.GetService(typeof(IHubContext<GatewayHub>)).Returns(hubContext);
        sp.GetService(typeof(IPetFactory)).Returns(petFactory);

        return (hubContext, petFactory, new MicroClaw.Sessions.SessionService(sp));
    }

    private static async Task StartServiceAsync(MicroClaw.Sessions.SessionService service)
    {
        var engine = new MicroEngine(new TestServiceProvider(), [service]);
        await engine.StartAsync();
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private void InitializeConfig(ChannelEntity[]? channels = null, SessionEntity[]? sessions = null)
    {
        ResetMicroClawConfig();

        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "config"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "workspace", "sessions"));
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);

        var data = new Dictionary<string, string?>
        {
            ["channel:channels"] = null,
            ["sessions:items"] = null
        };

        ChannelEntity[] effectiveChannels = channels ?? [];
        SessionEntity[] effectiveSessions = sessions ?? [];

        for (int i = 0; i < effectiveChannels.Length; i++)
        {
            ChannelEntity channel = effectiveChannels[i];
            data[$"channel:channels:{i}:id"] = channel.Id;
            data[$"channel:channels:{i}:display_name"] = channel.DisplayName;
            data[$"channel:channels:{i}:channel_type"] = ChannelService.SerializeChannelType(channel.ChannelType);
            data[$"channel:channels:{i}:is_enabled"] = channel.IsEnabled.ToString();
            data[$"channel:channels:{i}:setting_json"] = channel.SettingJson;
        }

        for (int i = 0; i < effectiveSessions.Length; i++)
        {
            SessionEntity session = effectiveSessions[i];
            data[$"sessions:items:{i}:id"] = session.Id;
            data[$"sessions:items:{i}:title"] = session.Title;
            data[$"sessions:items:{i}:provider_id"] = session.ProviderId;
            data[$"sessions:items:{i}:is_approved"] = session.IsApproved.ToString();
            data[$"sessions:items:{i}:channel_type"] = session.ChannelType;
            data[$"sessions:items:{i}:channel_id"] = session.ChannelId;
            data[$"sessions:items:{i}:created_at_ms"] = session.CreatedAtMs.ToString();
            data[$"sessions:items:{i}:agent_id"] = session.AgentId;
            data[$"sessions:items:{i}:approval_reason"] = session.ApprovalReason;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        MicroClawConfig.Initialize(configuration, Path.Combine(_tempRoot, "config"));
    }

    private static void ResetMicroClawConfig()
    {
        MethodInfo? resetMethod = typeof(MicroClawConfig).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic);
        resetMethod?.Invoke(null, null);
    }

    private sealed class FakeChannelProvider(ChannelType type, string displayName) : IChannelProvider
    {
        public string Name => displayName;

        public ChannelType Type => type;

        public string DisplayName => displayName;

        public IChannel Create(ChannelEntity config) => new FakeChannel(config, Name);

        public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
            IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
            => Task.FromResult(WebhookResult.Ok(null));

        public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelTestResult(true, "ok", 0));
    }

    private sealed class FakeChannel(ChannelEntity config, string name) : IChannel
    {
        public string Id => Config.Id;

        public string Name => name;

        public ChannelType Type => Config.ChannelType;

        public ChannelEntity Config { get; } = config;

        public string DisplayName => Config.DisplayName;

        public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WebhookResult> HandleWebhookAsync(string body,
            IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
            => Task.FromResult(WebhookResult.Ok(null));

        public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ChannelDiagnostics.Ok(Id, Type.ToString()));

        public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelTestResult(true, "ok", 0));
    }
}
