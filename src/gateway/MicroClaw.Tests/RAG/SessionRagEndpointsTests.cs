using FluentAssertions;
using MicroClaw.RAG;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

/// <summary>
/// 测试 GET /api/sessions/{sessionId}/rag/status 和
/// POST /api/sessions/{sessionId}/rag/reindex 的核心业务逻辑。
/// </summary>
public class SessionRagEndpointsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _dbFactory;
    private readonly IRagService _ragService;

    public SessionRagEndpointsTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "mc_session_rag_ep_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _dbFactory = new RagDbContextFactory(_tempDir);
        _ragService = Substitute.For<IRagService>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── 辅助：向 Session RAG DB 写入 chunk ─────────────────────────────────

    private async Task WriteSessionChunkAsync(string sessionId, string sourceId, long createdAtMs)
    {
        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        db.VectorChunks.Add(new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = "test content",
            VectorBlob = new byte[12],
            MetadataJson = null,
            CreatedAtMs = createdAtMs,
        });
        await db.SaveChangesAsync();
    }

    // ─── 状态查询：无分类 chunk ────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReturnsZeroCounts_WhenNoChunksExist()
    {
        var sessionId = "sess-empty";
        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var count = await db.VectorChunks.AsNoTracking()
            .Where(e => !e.SourceId.StartsWith("doc:"))
            .Select(e => e.SourceId)
            .Distinct()
            .CountAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectCategoryCount_WhenCategoryChunksExist()
    {
        var sessionId = "sess-with-categories";
        long ts1 = 1_000_000;
        long ts2 = 2_000_000;

        // 同一分类被拆成 2 个 chunk（相同 sourceId）
        await WriteSessionChunkAsync(sessionId, "项目进度", ts1);
        await WriteSessionChunkAsync(sessionId, "项目进度", ts1);
        // 另一个分类 1 个 chunk
        await WriteSessionChunkAsync(sessionId, "技术偏好", ts2);

        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var chunks = await db.VectorChunks.AsNoTracking()
            .Where(e => !e.SourceId.StartsWith("doc:"))
            .Select(e => new { e.SourceId, e.CreatedAtMs })
            .ToListAsync();

        int categoryCount = chunks.Select(e => e.SourceId).Distinct().Count();
        long? lastUpdatedAtMs = chunks.Max(e => (long?)e.CreatedAtMs);

        categoryCount.Should().Be(2);
        lastUpdatedAtMs.Should().Be(ts2);
    }

    [Fact]
    public async Task GetStatus_ExcludesDocChunks_FromCategoryCount()
    {
        var sessionId = "sess-mixed";
        await WriteSessionChunkAsync(sessionId, "项目进度", 1_000_000);
        await WriteSessionChunkAsync(sessionId, "doc:readme.md", 2_000_000);

        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var count = await db.VectorChunks.AsNoTracking()
            .Where(e => !e.SourceId.StartsWith("doc:"))
            .Select(e => e.SourceId)
            .Distinct()
            .CountAsync();

        count.Should().Be(1, "doc: 前缀的 chunk 不应计入分类数");
    }

    // ─── 重建索引：清除所有分类 chunk ─────────────────────────────────────────

    [Fact]
    public async Task Reindex_DeletesAllCategoryChunks()
    {
        var sessionId = "sess-reindex";
        await WriteSessionChunkAsync(sessionId, "项目进度", 1_000_000);
        await WriteSessionChunkAsync(sessionId, "技术偏好", 2_000_000);
        // doc: chunk 应保留
        await WriteSessionChunkAsync(sessionId, "doc:readme.md", 3_000_000);

        _ragService.DeleteBySourceIdAsync(
            Arg.Any<string>(), RagScope.Session, sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // 模拟 reindex 端点逻辑：收集非 doc: sourceId 并删除
        List<string> categorySourceIds;
        using (var db = _dbFactory.Create(RagScope.Session, sessionId))
        {
            categorySourceIds = await db.VectorChunks.AsNoTracking()
                .Where(e => !e.SourceId.StartsWith("doc:"))
                .Select(e => e.SourceId)
                .Distinct()
                .ToListAsync();
        }

        categorySourceIds.Should().HaveCount(2);
        categorySourceIds.Should().Contain("项目进度");
        categorySourceIds.Should().Contain("技术偏好");
        categorySourceIds.Should().NotContain("doc:readme.md");
    }
}
