using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Jobs;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// D-2: DreamingJob 单元测试。
/// 验证认知整理归因逻辑、记忆格式化、调度时间计算和跨会话 Agent DNA 更新。
/// </summary>
public sealed class DreamingJobTests : IDisposable
{
    private readonly TempDirectoryFixture _configDir = new();
    private readonly TempDirectoryFixture _sessionsDir = new();
    private readonly TempDirectoryFixture _agentsDir = new();
    private readonly MemoryService _memory;
    private readonly AgentDnaService _dna;

    public DreamingJobTests()
    {
        _memory = new MemoryService(_sessionsDir.Path);
        _dna = new AgentDnaService(_agentsDir.Path);
    }

    public void Dispose()
    {
        _configDir.Dispose();
        _sessionsDir.Dispose();
        _agentsDir.Dispose();
    }

    /// <summary>测试用 Logger：遇到 Error 级别日志时将异常重新抛出，避免 catch 块吞掉异常。</summary>
    private sealed class ThrowOnErrorLogger : ILogger<DreamingJob>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                throw new InvalidOperationException(
                    $"DreamingJob Log[{logLevel}]: {formatter(state, exception)}", exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // ── CalcDelayUntilNextRun ────────────────────────────────────────────────

    [Fact]
    public void CalcDelayUntilNextRun_BeforeRunTime_ReturnsDelayToday()
    {
        // 今天 02:00，RunTime=03:00 → 约 1 小时后
        DateTime now = new(2025, 8, 15, 2, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = DreamingJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalcDelayUntilNextRun_AfterRunTime_ReturnsDelayTomorrow()
    {
        // 今天 04:00，已过 03:00 → 下次约 23 小时后
        DateTime now = new(2025, 8, 15, 4, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = DreamingJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(23), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalcDelayUntilNextRun_AtExactRunTime_ReturnsOneDay()
    {
        // 恰好在 03:00，下次应在明天 03:00
        DateTime now = new(2025, 8, 15, 3, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = DreamingJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromSeconds(1));
    }

    // ── FormatSessionMemories ────────────────────────────────────────────────

    [Fact]
    public void FormatSessionMemories_SingleEntry_FormatsWithTitle()
    {
        var memories = new List<(string SessionTitle, string Content)>
        {
            ("代码审查会话", "[2025-08-14] - 发现了 3 处安全漏洞\n- 已提供修复建议")
        };

        string result = DreamingJob.FormatSessionMemories(memories);

        result.Should().Contain("### 会话「代码审查会话」");
        result.Should().Contain("[2025-08-14] - 发现了 3 处安全漏洞");
    }

    [Fact]
    public void FormatSessionMemories_MultipleEntries_JoinedWithBlankLine()
    {
        var memories = new List<(string SessionTitle, string Content)>
        {
            ("会话A", "[2025-08-13] - 内容A"),
            ("会话B", "[2025-08-14] - 内容B"),
        };

        string result = DreamingJob.FormatSessionMemories(memories);

        result.Should().Contain("### 会话「会话A」");
        result.Should().Contain("### 会话「会话B」");
        // 两个条目之间以两个换行分隔
        result.Should().Contain("\n\n");
    }

    [Fact]
    public void FormatSessionMemories_EmptyList_ReturnsEmpty()
    {
        string result = DreamingJob.FormatSessionMemories([]);

        result.Should().BeEmpty();
    }

    // ── BuildCognitiveDreamAsync ─────────────────────────────────────────────

    [Fact]
    public async Task BuildCognitiveDreamAsync_ReturnsLlmResponse()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "- 归因：用户倾向于提问后立即要代码\n- 策略：先理解意图再给示例"))));

        var fragments = new List<(string, string)>
        {
            ("会话1", "[2025-08-14] - 帮用户写了代码")
        };

        string result = await DreamingJob.BuildCognitiveDreamAsync(
            "现有记忆片段", fragments, client, CancellationToken.None);

        result.Should().Be("- 归因：用户倾向于提问后立即要代码\n- 策略：先理解意图再给示例");
    }

    [Fact]
    public async Task BuildCognitiveDreamAsync_EmptyExistingMemory_UsesPlaceholder()
    {
        var client = Substitute.For<IChatClient>();
        IList<ChatMessage>? captured = null;
        client.GetResponseAsync(
                Arg.Do<IList<ChatMessage>>(msgs => captured = msgs),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "结果"))));

        var fragments = new List<(string, string)>
        {
            ("测试会话", "[2025-08-14] - 某些记忆")
        };

        await DreamingJob.BuildCognitiveDreamAsync(string.Empty, fragments, client, CancellationToken.None);

        captured.Should().NotBeNull();
        captured![0].Text.Should().Contain("（暂无记忆）");
    }

    [Fact]
    public async Task BuildCognitiveDreamAsync_PromptContainsMemoriesContent()
    {
        var client = Substitute.For<IChatClient>();
        IList<ChatMessage>? captured = null;
        client.GetResponseAsync(
                Arg.Do<IList<ChatMessage>>(msgs => captured = msgs),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "ok"))));

        var fragments = new List<(string, string)>
        {
            ("重要会话", "[2025-08-12] - 用户反复询问同一问题")
        };

        await DreamingJob.BuildCognitiveDreamAsync("旧记忆", fragments, client, CancellationToken.None);

        captured.Should().NotBeNull();
        string prompt = captured![0].Text!;
        prompt.Should().Contain("重要会话");
        prompt.Should().Contain("用户反复询问同一问题");
        prompt.Should().Contain("旧记忆");
    }

    [Fact]
    public async Task BuildCognitiveDreamAsync_NullLlmText_ReturnsEmptyString()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse([])));

        var fragments = new List<(string, string)>
        {
            ("会话", "[2025-08-13] - 内容")
        };

        string result = await DreamingJob.BuildCognitiveDreamAsync(
            "记忆", fragments, client, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── RunDreamingAsync 集成测试 ────────────────────────────────────────────

    [Fact]
    public async Task RunDreamingAsync_DisabledAgent_IsSkipped()
    {
        string configDir = _configDir.Path;
        var agentStore = new AgentStore();
        var sessionStore = new SessionStore(_sessionsDir.Path);
        var providerStore = new ProviderConfigStore();

        // 创建已禁用的 Agent
        agentStore.Add(new AgentConfig(
            Id: Guid.NewGuid().ToString("N"),
            Name: "禁用代理",
            Description: "已禁用",
            IsEnabled: false,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

        var mockChatClient = Substitute.For<IChatClient>();
        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new DreamingJob(
            agentStore, sessionStore, providerStore, clientFactory,
            _dna, _memory, NullLogger<DreamingJob>.Instance);

        await job.RunDreamingAsync(CancellationToken.None);

        // LLM 不应被调用
        await mockChatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDreamingAsync_EnabledAgentWithNoSessions_IsSkipped()
    {
        string configDir = _configDir.Path;
        var agentStore = new AgentStore();
        var sessionStore = new SessionStore(_sessionsDir.Path);
        var providerStore = new ProviderConfigStore();

        // 创建已启用的 Agent，但不创建任何关联 Session
        agentStore.Add(new AgentConfig(
            Id: Guid.NewGuid().ToString("N"),
            Name: "孤立代理",
            Description: "无会话",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

        var mockChatClient = Substitute.For<IChatClient>();
        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new DreamingJob(
            agentStore, sessionStore, providerStore, clientFactory,
            _dna, _memory, NullLogger<DreamingJob>.Instance);

        await job.RunDreamingAsync(CancellationToken.None);

        await mockChatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDreamingAsync_AgentSessionsWithNoDailyMemories_IsSkipped()
    {
        string configDir = _configDir.Path;
        var agentStore = new AgentStore();
        var sessionStore = new SessionStore(_sessionsDir.Path);
        var providerStore = new ProviderConfigStore();

        // 创建 Agent
        string agentId = Guid.NewGuid().ToString("N");
        agentStore.Add(new AgentConfig(
            Id: agentId,
            Name: "无记忆代理",
            Description: "有会话但无日记忆",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

        // 创建 Provider 并创建关联 Session，但不写入任何日记忆
        ProviderConfig provider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "测试Provider",
            Protocol = ProviderProtocol.OpenAI,
            ModelName = "gpt-4o",
            ApiKey = "sk-test",
            IsEnabled = true,
        });
        sessionStore.Create("无记忆会话", provider.Id, ChannelType.Web, agentId: agentId);

        var mockChatClient = Substitute.For<IChatClient>();
        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new DreamingJob(
            agentStore, sessionStore, providerStore, clientFactory,
            _dna, _memory, NullLogger<DreamingJob>.Instance);

        await job.RunDreamingAsync(CancellationToken.None);

        await mockChatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDreamingAsync_AgentSessionsWithDailyMemories_UpdatesAgentDnaMemory()
    {
        string configDir = _configDir.Path;
        var agentStore = new AgentStore();
        var sessionStore = new SessionStore(_sessionsDir.Path);
        var providerStore = new ProviderConfigStore();

        // 创建 Agent（必须使用 Add 返回的 Config 以获取真实生成的 Id）
        AgentConfig createdAgent = agentStore.Add(new AgentConfig(
            Id: Guid.NewGuid().ToString("N"),
            Name: "认知整理代理",
            Description: "测试做梦功能",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));
        string agentId = createdAgent.Id;

        // 初始化 Agent DNA 目录（确保 MEMORY.md 可写入）
        _dna.InitializeAgent(agentId);

        // 创建 Provider 和关联 Session
        ProviderConfig provider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "测试Provider",
            Protocol = ProviderProtocol.OpenAI,
            ModelName = "gpt-4o",
            ApiKey = "sk-test",
            IsEnabled = true,
        });
        SessionInfo session = sessionStore.Create("有记忆会话", provider.Id, ChannelType.Web, agentId: agentId);

        // 写入一条昨天的日记忆
        string yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(session.Id, yesterday, "- 用户询问了 API 设计问题\n- 提供了 RESTful 最佳实践建议");

        // 模拟 LLM 返回认知归因摘要
        const string expectedSummary = "- 归因：用户关注 API 设计规范\n- 策略：优先提供标准化的架构建议";
        var mockChatClient = Substitute.For<IChatClient>();
        mockChatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, expectedSummary))));

        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new DreamingJob(
            agentStore, sessionStore, providerStore, clientFactory,
            _dna, _memory, new ThrowOnErrorLogger());

        await job.RunDreamingAsync(CancellationToken.None);

        // 1. 验证 LLM 被调用了（说明 agentSessions 过滤和 provider 解析均成功）
        await mockChatClient.Received(1).GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());

        // 2. 直接验证 MEMORY.md 文件已被写入
        string memoryFilePath = Path.Combine(_agentsDir.Path, agentId, "MEMORY.md");
        File.Exists(memoryFilePath).Should().BeTrue("UpdateMemory 应当写入 MEMORY.md 文件");

        // 3. 验证 Agent MEMORY.md 已被更新为 LLM 输出的认知归因
        string agentMemory = _dna.GetMemory(agentId);
        agentMemory.Should().Be(expectedSummary);
    }

    [Fact]
    public async Task RunDreamingAsync_NoEnabledProviderAvailable_IsSkipped()
    {
        string configDir = _configDir.Path;
        var agentStore = new AgentStore();
        var sessionStore = new SessionStore(_sessionsDir.Path);
        var providerStore = new ProviderConfigStore();

        // 创建 Agent
        string agentId = Guid.NewGuid().ToString("N");
        agentStore.Add(new AgentConfig(
            Id: agentId,
            Name: "无Provider代理",
            Description: "无可用 Provider",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

        // 创建一个已禁用的 Provider
        ProviderConfig disabledProvider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "禁用Provider",
            Protocol = ProviderProtocol.OpenAI,
            ModelName = "gpt-4o",
            ApiKey = "sk-test",
            IsEnabled = false,
        });

        // 创建关联 Session
        SessionInfo session = sessionStore.Create("会话", disabledProvider.Id, ChannelType.Web, agentId: agentId);

        // 写入日记忆使流程推进到 ResolveClient 步骤
        string yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(session.Id, yesterday, "- 有内容但无可用Provider");

        var mockChatClient = Substitute.For<IChatClient>();
        var clientFactory = new ProviderClientFactory([]);  // 空的，不支持任何协议

        var job = new DreamingJob(
            agentStore, sessionStore, providerStore, clientFactory,
            _dna, _memory, NullLogger<DreamingJob>.Instance);

        await job.RunDreamingAsync(CancellationToken.None);

        // LLM 不应被调用
        await mockChatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }
}

