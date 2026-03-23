using FluentAssertions;
using MicroClaw.Agent.Memory;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

public sealed class MemoryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _dir = new();
    private readonly MemoryService _svc;
    private const string SessionId = "session1";

    public MemoryServiceTests()
    {
        _svc = new MemoryService(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    // ── IsValidDateFormat ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("2025-01-01")]
    [InlineData("2024-12-31")]
    [InlineData("2026-03-23")]
    public void IsValidDateFormat_ValidDates_ReturnsTrue(string date)
    {
        MemoryService.IsValidDateFormat(date).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2025-1-1")]       // 非 zero-padded
    [InlineData("25-01-01")]       // 两位年
    [InlineData("2025/01/01")]     // 斜线
    [InlineData("not-a-date")]
    [InlineData("2025-13-01")]     // 月份超界
    public void IsValidDateFormat_InvalidDates_ReturnsFalse(string date)
    {
        MemoryService.IsValidDateFormat(date).Should().BeFalse();
    }

    // ── GetLongTermMemory ─────────────────────────────────────────────────────

    [Fact]
    public void GetLongTermMemory_WhenFileNotExist_ReturnsEmptyString()
    {
        _svc.GetLongTermMemory(SessionId).Should().BeEmpty();
    }

    [Fact]
    public void GetLongTermMemory_WhenFileExists_ReturnsContent()
    {
        _svc.UpdateLongTermMemory(SessionId, "长期记忆内容");

        _svc.GetLongTermMemory(SessionId).Should().Be("长期记忆内容");
    }

    // ── UpdateLongTermMemory ──────────────────────────────────────────────────

    [Fact]
    public void UpdateLongTermMemory_CreatesFileAndDirIfNotExist()
    {
        _svc.UpdateLongTermMemory(SessionId, "内容ABC");

        string path = Path.Combine(_dir.Path, SessionId, "MEMORY.md");
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Be("内容ABC");
    }

    [Fact]
    public void UpdateLongTermMemory_OverwritesExistingContent()
    {
        _svc.UpdateLongTermMemory(SessionId, "初始内容");
        _svc.UpdateLongTermMemory(SessionId, "更新内容");

        _svc.GetLongTermMemory(SessionId).Should().Be("更新内容");
    }

    // ── GetDailyMemory ────────────────────────────────────────────────────────

    [Fact]
    public void GetDailyMemory_WhenFileNotExist_ReturnsNull()
    {
        _svc.GetDailyMemory(SessionId, "2025-01-01").Should().BeNull();
    }

    [Fact]
    public void GetDailyMemory_InvalidDateFormat_ReturnsNull()
    {
        _svc.GetDailyMemory(SessionId, "not-a-date").Should().BeNull();
    }

    [Fact]
    public void GetDailyMemory_WhenExists_ReturnsContent()
    {
        _svc.WriteDailyMemory(SessionId, "2025-01-15", "今天记忆内容");

        DailyMemoryInfo? info = _svc.GetDailyMemory(SessionId, "2025-01-15");

        info.Should().NotBeNull();
        info!.Date.Should().Be("2025-01-15");
        info.Content.Should().Be("今天记忆内容");
    }

    // ── WriteDailyMemory ──────────────────────────────────────────────────────

    [Fact]
    public void WriteDailyMemory_CreatesFileInSubDir()
    {
        _svc.WriteDailyMemory(SessionId, "2025-06-01", "内容");

        string path = Path.Combine(_dir.Path, SessionId, "memory", "2025-06-01.md");
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Be("内容");
    }

    [Fact]
    public void WriteDailyMemory_InvalidDate_ThrowsArgumentException()
    {
        Action act = () => _svc.WriteDailyMemory(SessionId, "bad-date", "内容");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*bad-date*");
    }

    [Fact]
    public void WriteDailyMemory_OverwritesExistingContent()
    {
        _svc.WriteDailyMemory(SessionId, "2025-03-10", "旧内容");
        _svc.WriteDailyMemory(SessionId, "2025-03-10", "新内容");

        DailyMemoryInfo? info = _svc.GetDailyMemory(SessionId, "2025-03-10");
        info!.Content.Should().Be("新内容");
    }

    // ── ListDailyMemories ─────────────────────────────────────────────────────

    [Fact]
    public void ListDailyMemories_WhenNone_ReturnsEmpty()
    {
        _svc.ListDailyMemories(SessionId).Should().BeEmpty();
    }

    [Fact]
    public void ListDailyMemories_ReturnsDescendingOrder()
    {
        _svc.WriteDailyMemory(SessionId, "2025-01-01", "A");
        _svc.WriteDailyMemory(SessionId, "2025-03-15", "B");
        _svc.WriteDailyMemory(SessionId, "2025-02-10", "C");

        IReadOnlyList<string> dates = _svc.ListDailyMemories(SessionId);

        dates.Should().ContainInOrder("2025-03-15", "2025-02-10", "2025-01-01");
    }

    // ── BuildMemoryContext ────────────────────────────────────────────────────

    [Fact]
    public void BuildMemoryContext_WhenNoMemory_ReturnsEmptyString()
    {
        _svc.BuildMemoryContext(SessionId).Should().BeEmpty();
    }

    [Fact]
    public void BuildMemoryContext_WithLongTermOnly_ContainsLongTermSection()
    {
        _svc.UpdateLongTermMemory(SessionId, "长期记忆内容XXX");

        string ctx = _svc.BuildMemoryContext(SessionId);

        ctx.Should().Contain("长期记忆").And.Contain("长期记忆内容XXX");
    }

    [Fact]
    public void BuildMemoryContext_RecentDailyMemory_FullContent()
    {
        // 写入今天的记忆
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _svc.WriteDailyMemory(SessionId, today, "今天全量内容");

        string ctx = _svc.BuildMemoryContext(SessionId);

        ctx.Should().Contain("近期记忆").And.Contain("今天全量内容");
    }

    [Fact]
    public void BuildMemoryContext_OldDailyMemory_OnlyFirstLine()
    {
        // 写入 15 天前的记忆（7 < 15 <= 30，应该只取首行）
        string oldDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)).ToString("yyyy-MM-dd");
        _svc.WriteDailyMemory(SessionId, oldDate, "摘要首行\n\n这行不会出现");

        string ctx = _svc.BuildMemoryContext(SessionId);

        ctx.Should().Contain("历史记忆摘要").And.Contain("摘要首行");
        ctx.Should().NotContain("这行不会出现");
    }

    [Fact]
    public void BuildMemoryContext_VeryOldMemory_Ignored()
    {
        // 写入 35 天前的记忆（> 30 天，应该忽略）
        string veryOldDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-35)).ToString("yyyy-MM-dd");
        _svc.WriteDailyMemory(SessionId, veryOldDate, "35天前的记忆，不应出现");

        string ctx = _svc.BuildMemoryContext(SessionId);

        ctx.Should().NotContain("35天前的记忆");
    }
}
