using System.Reflection;
using FluentAssertions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Hubs;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

/// <summary>
/// 覆盖 <see cref="SessionService"/> 作为 <see cref="MicroService"/> 的生命周期行为：
/// 启停对 <see cref="SessionMessagesComponent"/> 的挂接与释放、<c>CreateSession</c> 自动挂载组件。
/// </summary>
public sealed class SessionServiceMicroTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_AttachesMessagesComponentToEachWarmedSession()
    {
        InitializeConfig(
        [
            new SessionEntity
            {
                Id = "s1",
                Title = "T1",
                ProviderId = "p",
                ChannelType = "web",
                ChannelId = "web",
                CreatedAtMs = 1,
            },
            new SessionEntity
            {
                Id = "s2",
                Title = "T2",
                ProviderId = "p",
                ChannelType = "web",
                ChannelId = "web",
                CreatedAtMs = 2,
            },
        ]);

        (SessionService service, _) = CreateSessionService();
        MicroEngine engine = new(new TestServiceProvider(), [service]);

        await engine.StartAsync();
        try
        {
            ISessionService repo = service;
            foreach (IMicroSession s in repo.GetAll())
            {
                MicroSession micro = (MicroSession)s;
                micro.GetComponent<SessionMessagesComponent>().Should().NotBeNull();
            }
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_DisposesEachSessionAndReleasesComponents()
    {
        InitializeConfig(
        [
            new SessionEntity
            {
                Id = "s-stop",
                Title = "T",
                ProviderId = "p",
                ChannelType = "web",
                ChannelId = "web",
                CreatedAtMs = 1,
            },
        ]);

        (SessionService service, _) = CreateSessionService();
        MicroEngine engine = new(new TestServiceProvider(), [service]);

        await engine.StartAsync();
        MicroSession session = (MicroSession)((ISessionService)service).Get("s-stop")!;
        SessionMessagesComponent component = session.GetComponent<SessionMessagesComponent>()!;

        await engine.StopAsync();

        session.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        component.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        ((ISessionService)service).GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSession_AutoAttachesMessagesComponent()
    {
        InitializeConfig([]);

        (SessionService service, _) = CreateSessionService();
        MicroEngine engine = new(new TestServiceProvider(), [service]);

        await engine.StartAsync();
        try
        {
            IMicroSession created = await service.CreateSession(title: "t", providerId: "p");
            MicroSession micro = (MicroSession)created;
            micro.GetComponent<SessionMessagesComponent>().Should().NotBeNull();
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Delete_DisposesSessionAndReleasesComponents()
    {
        InitializeConfig(
        [
            new SessionEntity
            {
                Id = "s-delete",
                Title = "T",
                ProviderId = "p",
                ChannelType = "web",
                ChannelId = "web",
                CreatedAtMs = 1,
            },
        ]);

        (SessionService service, _) = CreateSessionService();
        MicroEngine engine = new(new TestServiceProvider(), [service]);

        await engine.StartAsync();
        try
        {
            MicroSession session = (MicroSession)((ISessionService)service).Get("s-delete")!;
            SessionMessagesComponent component = session.GetComponent<SessionMessagesComponent>()!;

            ((ISessionService)service).Delete("s-delete").Should().BeTrue();

            session.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
            component.LifeCycleState.Should().Be(MicroLifeCycleState.Disposed);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    public void Dispose()
    {
        ResetMicroClawConfig();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private (SessionService Service, IPetFactory PetFactory) CreateSessionService()
    {
        IHubContext<GatewayHub> hubContext = Substitute.For<IHubContext<GatewayHub>>();
        IPetFactory petFactory = Substitute.For<IPetFactory>();
        petFactory.CreateOrLoadAsync(Arg.Any<IMicroSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IPet?>(null));

        MicroClaw.Agent.AgentStore agentStore = new();

        IServiceProvider sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(MicroClaw.Agent.AgentStore)).Returns(agentStore);
        sp.GetService(typeof(IHubContext<GatewayHub>)).Returns(hubContext);
        sp.GetService(typeof(IPetFactory)).Returns(petFactory);

        return (new SessionService(sp), petFactory);
    }

    private void InitializeConfig(SessionEntity[] sessions)
    {
        ResetMicroClawConfig();
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "config"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "workspace", "sessions"));
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);

        Dictionary<string, string?> data = new();
        for (int i = 0; i < sessions.Length; i++)
        {
            SessionEntity e = sessions[i];
            data[$"sessions:items:{i}:id"] = e.Id;
            data[$"sessions:items:{i}:title"] = e.Title;
            data[$"sessions:items:{i}:provider_id"] = e.ProviderId;
            data[$"sessions:items:{i}:is_approved"] = e.IsApproved.ToString();
            data[$"sessions:items:{i}:channel_type"] = e.ChannelType;
            data[$"sessions:items:{i}:channel_id"] = e.ChannelId;
            data[$"sessions:items:{i}:created_at_ms"] = e.CreatedAtMs.ToString();
            data[$"sessions:items:{i}:agent_id"] = e.AgentId;
            data[$"sessions:items:{i}:approval_reason"] = e.ApprovalReason;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        MicroClawConfig.Initialize(configuration, Path.Combine(_tempRoot, "config"));
    }

    private static void ResetMicroClawConfig()
    {
        MethodInfo? resetMethod = typeof(MicroClawConfig).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic);
        resetMethod?.Invoke(null, null);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
