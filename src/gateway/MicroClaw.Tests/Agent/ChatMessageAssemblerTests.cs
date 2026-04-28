using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Restorers;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Providers;
using MicroClaw.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agent;

public sealed class ChatMessageAssemblerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AssembleAsync_WithPetDecorations_ReturnsOrderedMessages()
    {
        Directory.CreateDirectory(_tempRoot);
        string configDir = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(configDir);
        string skillRoot = Path.Combine(_tempRoot, "skills");
        Directory.CreateDirectory(skillRoot);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        MicroClawConfig.Initialize(configuration, configDir);

        SkillService skillService = new(_tempRoot, [skillRoot]);
        SkillStore skillStore = new(skillService);
        SkillToolFactory skillToolFactory = new(skillStore, skillService);

        IAgentContextProvider baseProvider = Substitute.For<IAgentContextProvider>();
        baseProvider.Order.Returns(10);
        baseProvider.BuildContextAsync(Arg.Any<MicroClaw.Agent.Agent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string?>("base context"));

        IUserAwareContextProvider userAwareProvider = Substitute.For<IUserAwareContextProvider>();
        userAwareProvider.Order.Returns(20);
        userAwareProvider.BuildContextAsync(
                Arg.Any<MicroClaw.Agent.Agent>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<string?>($"rag:{call.ArgAt<string?>(2)}"));

        IContextOverflowSummarizer summarizer = Substitute.For<IContextOverflowSummarizer>();
        summarizer.SummarizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionMessage>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var assembler = new ChatMessageAssembler(
            [baseProvider, userAwareProvider],
            skillToolFactory,
            new ChatContentRestorerService(),
            summarizer,
            NullLogger<ChatMessageAssembler>.Instance);

        MicroClaw.Agent.Agent agent = MicroClaw.Agent.Agent.Reconstitute(
            id: "agent-1",
            name: "Agent 1",
            description: "test",
            isEnabled: true,
            disabledSkillIds: [],
            disabledMcpServerIds: [],
            toolGroupConfigs: [],
            createdAtUtc: DateTimeOffset.UtcNow);
        var provider = new ProviderConfig
        {
            Id = "provider-1",
            DisplayName = "Provider 1",
            ModelType = ModelType.Chat,
            ModelName = "model-1",
            Capabilities = new ProviderCapabilities()
        };
        IReadOnlyList<SessionMessage> history =
        [
            new SessionMessage(
                Id: "msg-1",
                Role: "user",
                Content: "hello",
                ThinkContent: null,
                Timestamp: DateTimeOffset.UtcNow,
                Attachments: null)
        ];

        ChatMessageAssemblyResult result = await assembler.AssembleAsync(
            agent,
            provider,
            history,
            sessionId: "session-1",
            behaviorSuffix: "be calm",
            petKnowledge: "pet memo");

        result.SkillContext.CatalogFragment.Should().BeEmpty();
        result.Messages.Should().HaveCount(3);

        result.Messages[0].Role.Should().Be(ChatRole.System);
        result.Messages[0].Text.Should().Contain("base context");
        result.Messages[0].Text.Should().Contain("rag:hello");
        result.Messages[0].Text.Should().Contain("be calm");

        result.Messages[1].Role.Should().Be(ChatRole.System);
        result.Messages[1].Text.Should().Contain("pet memo");

        result.Messages[2].Role.Should().Be(ChatRole.User);
        result.Messages[2].MessageId.Should().Be("msg-1");
        result.Messages[2].Text.Should().Be("hello");
        result.Messages[2].Contents.Should().ContainSingle();
        result.Messages[2].Contents[0].Should().BeOfType<TextContent>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}