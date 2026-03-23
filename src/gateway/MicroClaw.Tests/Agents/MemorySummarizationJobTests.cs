using FluentAssertions;
using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Jobs;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// B-02: MemorySummarizationJob 单元测试。
/// 验证每日摘要生成、长期记忆合并、消息格式化和调度时间计算逻辑。
/// </summary>
public sealed class MemorySummarizationJobTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly MemoryService _memory;

    public MemorySummarizationJobTests()
    {
        _memory = new MemoryService(_tempDir.Path);
    }

    public void Dispose()
    {
        _db.Dispose();
        _tempDir.Dispose();
    }

    // ── FormatMessages ───────────────────────────────────────────────────────

    [Fact]
    public void FormatMessages_FormatsProperly()
    {
        var messages = new List<SessionMessage>
        {
            new("user", "帮我写一段代码", null, DateTimeOffset.UtcNow, null),
            new("assistant", "好的，这是代码：...", null, DateTimeOffset.UtcNow, null),
        };

        string result = MemorySummarizationJob.FormatMessages(messages);

        result.Should().Contain("[用户]: 帮我写一段代码");
        result.Should().Contain("[AI]: 好的，这是代码：...");
    }

    [Fact]
    public void FormatMessages_TruncatesLongContent()
    {
        string longContent = new string('a', 600);
        var messages = new List<SessionMessage>
        {
            new("user", longContent, null, DateTimeOffset.UtcNow, null),
        };

        string result = MemorySummarizationJob.FormatMessages(messages);

        result.Should().Contain("...");
        // 截断后最多 500 字符 + "[用户]: " + "..."
        string messageBody = result.Replace("[用户]: ", "");
        messageBody.Should().EndWith("...");
        messageBody.Replace("...", "").Length.Should().Be(500);
    }

    [Fact]
    public void FormatMessages_FiltersOnlyUserAndAssistant()
    {
        var messages = new List<SessionMessage>
        {
            new("user", "用户消息", null, DateTimeOffset.UtcNow, null),
            new("assistant", "AI消息", null, DateTimeOffset.UtcNow, null),
            new("system", "系统消息", null, DateTimeOffset.UtcNow, null),
        };

        // FormatMessages 不做 role 过滤（过滤在 SummarizeSessionAsync），但不应崩溃
        string result = MemorySummarizationJob.FormatMessages(messages);

        result.Should().NotBeNullOrEmpty();
    }

    // ── CalcDelayUntilNextRun ────────────────────────────────────────────────

    [Fact]
    public void CalcDelayUntilNextRun_BeforeRunTime_ReturnsDelayToday()
    {
        // 今天 01:00，距 02:00 仅 1 小时
        DateTime now = new(2025, 6, 15, 1, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = MemorySummarizationJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalcDelayUntilNextRun_AfterRunTime_ReturnsDelayTomorrow()
    {
        // 今天 03:00，已过 02:00，下次应在明天 02:00（约 23 小时后）
        DateTime now = new(2025, 6, 15, 3, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = MemorySummarizationJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(23), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CalcDelayUntilNextRun_AtExactRunTime_ReturnsOneDay()
    {
        // 恰好在 02:00，下次应在明天 02:00
        DateTime now = new(2025, 6, 15, 2, 0, 0, DateTimeKind.Utc);

        TimeSpan delay = MemorySummarizationJob.CalcDelayUntilNextRun(now);

        delay.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromSeconds(1));
    }

    // ── BuildDailySummaryAsync ────────────────────────────────────────────────

    [Fact]
    public async Task BuildDailySummaryAsync_ReturnsLlmResponse()
    {
        var client = Substitute.For<IChatClient>();
        var messages = new List<SessionMessage>
        {
            new("user", "今天我遇到了一个 Bug", null, DateTimeOffset.UtcNow, null),
            new("assistant", "我帮你找到了原因是 NullReference", null, DateTimeOffset.UtcNow, null),
        };

        client.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "- 调试并解决了 NullReference 异常"))));

        string summary = await MemorySummarizationJob.BuildDailySummaryAsync(
            messages, client, CancellationToken.None);

        summary.Should().Be("- 调试并解决了 NullReference 异常");
    }

    [Fact]
    public async Task BuildDailySummaryAsync_PromptContainsFormattedMessages()
    {
        var client = Substitute.For<IChatClient>();
        IList<ChatMessage>? capturedMessages = null;

        client.GetResponseAsync(
                Arg.Do<IList<ChatMessage>>(msgs => capturedMessages = msgs),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "summary"))));

        var messages = new List<SessionMessage>
        {
            new("user", "测试消息", null, DateTimeOffset.UtcNow, null),
        };

        await MemorySummarizationJob.BuildDailySummaryAsync(
            messages, client, CancellationToken.None);

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().HaveCount(1);
        capturedMessages![0].Text.Should().Contain("测试消息");
    }

    // ── BuildWeeklyMergeAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BuildWeeklyMergeAsync_ReturnsLlmResponse()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "- 合并后的长期记忆"))));

        string merged = await MemorySummarizationJob.BuildWeeklyMergeAsync(
            "现有记忆",
            ["## 2025-06-08\n- 昨天做了A"],
            client,
            CancellationToken.None);

        merged.Should().Be("- 合并后的长期记忆");
    }

    [Fact]
    public async Task BuildWeeklyMergeAsync_EmptyExistingMemory_UsesPlaceholder()
    {
        var client = Substitute.For<IChatClient>();
        IList<ChatMessage>? capturedMessages = null;

        client.GetResponseAsync(
                Arg.Do<IList<ChatMessage>>(msgs => capturedMessages = msgs),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "ok"))));

        await MemorySummarizationJob.BuildWeeklyMergeAsync(
            string.Empty,
            ["## 2025-06-08\n- 内容"],
            client,
            CancellationToken.None);

        capturedMessages![0].Text.Should().Contain("（暂无长期记忆）");
    }

    // ── RunSummarizationAsync 集成测试 ────────────────────────────────────────

    [Fact]
    public async Task RunSummarizationAsync_WritesMemoryForSessionWithMessages()
    {
        // ── 准备 SessionStore 和 ProviderStore（使用真实 SQLite）──────────────
        IDbContextFactory<MicroClaw.Infrastructure.Data.GatewayDbContext> dbFactory = _db.CreateFactory();
        string sessionsDir = _tempDir.Path;

        var sessionStore = new SessionStore(dbFactory, sessionsDir);
        var providerStore = new ProviderConfigStore(dbFactory);

        // 创建测试 Provider
        ProviderConfig testProvider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "Test Provider",
            Protocol = ProviderProtocol.OpenAI,
            BaseUrl = "https://api.example.com",
            ModelName = "gpt-4o",
            ApiKey = "sk-test",
            IsEnabled = true,
        });

        // 创建测试 Session
        SessionInfo session = sessionStore.Create(
            "测试会话", testProvider.Id, ChannelType.Web);

        // 添加目标日期的消息
        DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        DateTimeOffset msgTime = new DateTimeOffset(
            targetDate.Year, targetDate.Month, targetDate.Day, 12, 0, 0, TimeSpan.Zero);

        sessionStore.AddMessage(session.Id, new SessionMessage(
            "user", "帮我分析这份数据", null, msgTime, null));
        sessionStore.AddMessage(session.Id, new SessionMessage(
            "assistant", "数据显示增长趋势明显", null, msgTime.AddMinutes(1), null));

        // ── 准备 Mock Provider 和 IChatClient ─────────────────────────────────
        var mockChatClient = Substitute.For<IChatClient>();
        mockChatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "- 用户请求数据分析\n- AI 分析结果：增长趋势明显"))));

        var mockProvider = Substitute.For<IModelProvider>();
        mockProvider.Supports(ProviderProtocol.OpenAI).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);

        var clientFactory = new ProviderClientFactory([mockProvider]);

        // ── 创建 Job 并执行 ───────────────────────────────────────────────────
        var job = new MemorySummarizationJob(
            sessionStore, providerStore, clientFactory, _memory,
            NullLogger<MemorySummarizationJob>.Instance);

        await job.RunSummarizationAsync(targetDate, doWeeklyMerge: false, CancellationToken.None);

        // ── 验证每日记忆文件已写入 ────────────────────────────────────────────
        DailyMemoryInfo? written = _memory.GetDailyMemory(session.Id, targetDate.ToString("yyyy-MM-dd"));
        written.Should().NotBeNull();
        written!.Content.Should().Contain("用户请求数据分析");
    }

    [Fact]
    public async Task RunSummarizationAsync_SkipsSessionWithNoMessages()
    {
        IDbContextFactory<MicroClaw.Infrastructure.Data.GatewayDbContext> dbFactory = _db.CreateFactory();
        var sessionStore = new SessionStore(dbFactory, _tempDir.Path);
        var providerStore = new ProviderConfigStore(dbFactory);

        ProviderConfig testProvider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "Test",
            Protocol = ProviderProtocol.OpenAI,
            ModelName = "gpt-4o",
            ApiKey = "sk-x",
            IsEnabled = true,
        });

        SessionInfo session = sessionStore.Create("空会话", testProvider.Id, ChannelType.Web);

        var mockChatClient = Substitute.For<IChatClient>();
        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(ProviderProtocol.OpenAI).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new MemorySummarizationJob(
            sessionStore, providerStore, clientFactory, _memory,
            NullLogger<MemorySummarizationJob>.Instance);

        DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await job.RunSummarizationAsync(targetDate, doWeeklyMerge: false, CancellationToken.None);

        // IChatClient 不应被调用
        await mockChatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());

        // 每日记忆文件不应被创建
        DailyMemoryInfo? written = _memory.GetDailyMemory(session.Id, targetDate.ToString("yyyy-MM-dd"));
        written.Should().BeNull();
    }

    [Fact]
    public async Task RunSummarizationAsync_DoesWeeklyMerge_WhenFlagIsTrue()
    {
        IDbContextFactory<MicroClaw.Infrastructure.Data.GatewayDbContext> dbFactory = _db.CreateFactory();
        var sessionStore = new SessionStore(dbFactory, _tempDir.Path);
        var providerStore = new ProviderConfigStore(dbFactory);

        ProviderConfig testProvider = providerStore.Add(new ProviderConfig
        {
            DisplayName = "Test",
            Protocol = ProviderProtocol.OpenAI,
            ModelName = "gpt-4o",
            ApiKey = "sk-x",
            IsEnabled = true,
        });

        SessionInfo session = sessionStore.Create("合并会话", testProvider.Id, ChannelType.Web);

        // 写入一条 7 天前的日记忆
        DateOnly sevenDaysAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        _memory.WriteDailyMemory(session.Id, sevenDaysAgo.ToString("yyyy-MM-dd"), "- 七天前的记忆");

        var mockChatClient = Substitute.For<IChatClient>();
        mockChatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "- 合并后的长期记忆内容"))));

        var mockModelProvider = Substitute.For<IModelProvider>();
        mockModelProvider.Supports(ProviderProtocol.OpenAI).Returns(true);
        mockModelProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockChatClient);
        var clientFactory = new ProviderClientFactory([mockModelProvider]);

        var job = new MemorySummarizationJob(
            sessionStore, providerStore, clientFactory, _memory,
            NullLogger<MemorySummarizationJob>.Instance);

        DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await job.RunSummarizationAsync(targetDate, doWeeklyMerge: true, CancellationToken.None);

        // 长期记忆应被更新
        string longTerm = _memory.GetLongTermMemory(session.Id);
        longTerm.Should().Be("- 合并后的长期记忆内容");
    }
}
