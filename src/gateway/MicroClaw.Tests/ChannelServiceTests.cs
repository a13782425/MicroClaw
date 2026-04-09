using System.Reflection;
using FluentAssertions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
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
        InitializeConfig(new ChannelEntity
        {
            Id = "feishu-a",
            DisplayName = "Feishu A",
            ChannelType = ChannelType.Feishu,
            IsEnabled = true,
            SettingJson = """{"appId":"a"}"""
        });

        var service = new ChannelService(new ChannelConfigStore(), [new FakeChannelProvider(ChannelType.Feishu, "Feishu")]);

        IChannel first = service.GetRequired("feishu-a");
        IChannel second = service.GetRequired("feishu-a");

        ReferenceEquals(first, second).Should().BeTrue();
        first.Config.DisplayName.Should().Be("Feishu A");
    }

    [Fact]
    public void GetRequired_DifferentChannelConfigs_ReturnDistinctInstances()
    {
        InitializeConfig(
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
            });

        var service = new ChannelService(new ChannelConfigStore(), [new FakeChannelProvider(ChannelType.Feishu, "Feishu")]);

        IChannel first = service.GetRequired("feishu-a");
        IChannel second = service.GetRequired("feishu-b");

        ReferenceEquals(first, second).Should().BeFalse();
        first.Config.SettingJson.Should().Contain("a");
        second.Config.SettingJson.Should().Contain("b");
    }

    [Fact]
    public void SessionService_CreateSession_DoesNotAttachChannelAutomatically()
    {
        InitializeConfig(new ChannelEntity
        {
            Id = ChannelConfigStore.WebChannelId,
            DisplayName = "Web Console",
            ChannelType = ChannelType.Web,
            IsEnabled = true,
            SettingJson = "{}"
        });

        var hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var petFactory = Substitute.For<IPetFactory>();
        petFactory.CreateOrLoadAsync(Arg.Any<MicroClaw.Abstractions.Sessions.IMicroSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IPet?>(null));

        var agentStore = new MicroClaw.Agent.AgentStore();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(MicroClaw.Agent.AgentStore)).Returns(agentStore);
        sp.GetService(typeof(IHubContext<GatewayHub>)).Returns(hubContext);
        sp.GetService(typeof(IPetFactory)).Returns(petFactory);

        var service = new MicroClaw.Sessions.SessionService(sp);

        Session session = service.CreateSession("test", "provider-1");

        session.Channel.Should().BeNull();
    }

    public void Dispose()
    {
        ResetMicroClawConfig();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private void InitializeConfig(params ChannelEntity[] channels)
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

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelEntity channel = channels[i];
            data[$"channel:channels:{i}:id"] = channel.Id;
            data[$"channel:channels:{i}:display_name"] = channel.DisplayName;
            data[$"channel:channels:{i}:channel_type"] = ChannelConfigStore.SerializeChannelType(channel.ChannelType);
            data[$"channel:channels:{i}:is_enabled"] = channel.IsEnabled.ToString();
            data[$"channel:channels:{i}:setting_json"] = channel.SettingJson;
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

        public Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

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

        public Task<string?> HandleWebhookAsync(string body, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelTestResult(true, "ok", 0));
    }
}
