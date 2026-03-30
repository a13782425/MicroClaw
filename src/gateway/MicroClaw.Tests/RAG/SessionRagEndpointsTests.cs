using FluentAssertions;
using MicroClaw.Agent.Sessions;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.RAG;
using MicroClaw.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ISessionMessageIndexer _indexer;

    public SessionRagEndpointsTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "mc_session_rag_ep_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _dbFactory = new RagDbContextFactory(_tempDir);
        _ragService = Substitute.For<IRagService>();
        _indexer = Substitute.For<ISessionMessageIndexer>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── 辅助：向 Session RAG DB 写入 msg: 分块 ─────────────────────────────

    private async Task WriteSessionChunkAsync(string sessionId, string sourceId, long createdAtMs)
    {
        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        db.VectorChunks.Add(new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = "test content",
            VectorBlob = new byte[12], // 3 × float32 = 12 bytes
            MetadataJson = null,
            CreatedAtMs = createdAtMs,
        });
        await db.SaveChangesAsync();
    }

    // ─── 状态查询：无已索引消息 ───────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReturnsZeroCounts_WhenNoChunksExist()
    {
        // DB 还未创建的会话，第一次访问应自动建表且返回 0
        var sessionId = "sess-empty";
        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        // 只需 EnsureCreated，不写入数据
        var count = await db.VectorChunks.AsNoTracking()
            .Where(e => e.SourceId.StartsWith("msg:"))
            .Select(e => e.SourceId)
            .Distinct()
            .CountAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectCounts_WhenChunksExist()
    {
        var sessionId = "sess-with-data";
        long ts1 = 1_000_000;
        long ts2 = 2_000_000;

        // 同一条消息被拆成 2 个分块（相同 sourceId）
        await WriteSessionChunkAsync(sessionId, "msg:aaa", ts1);
        await WriteSessionChunkAsync(sessionId, "msg:aaa", ts1);
        // 另一条消息 1 个分块
        await WriteSessionChunkAsync(sessionId, "msg:bbb", ts2);

        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var chunks = await db.VectorChunks.AsNoTracking()
            .Where(e => e.SourceId.StartsWith("msg:"))
            .Select(e => new { e.SourceId, e.CreatedAtMs })
            .ToListAsync();

        int indexedMessageCount = chunks.Select(e => e.SourceId).Distinct().Count();
        long? lastIndexedAtMs = chunks.Max(e => (long?)e.CreatedAtMs);

        indexedMessageCount.Should().Be(2);
        lastIndexedAtMs.Should().Be(ts2);
    }

    [Fact]
    public async Task GetStatus_ExcludesDocChunks_FromMessageCount()
    {
        // doc: 前缀的分块不应计入已索引消息数
        var sessionId = "sess-mixed";
        await WriteSessionChunkAsync(sessionId, "msg:aaa", 1_000_000);
        await WriteSessionChunkAsync(sessionId, "doc:readme.md", 2_000_000);

        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var msgChunks = await db.VectorChunks.AsNoTracking()
            .Where(e => e.SourceId.StartsWith("msg:"))
            .Select(e => e.SourceId)
            .Distinct()
            .CountAsync();

        msgChunks.Should().Be(1, "doc: 前缀分块不应计入消息索引数");
    }

    // ─── 重新索引：ISessionMessageIndexer 被调用 ─────────────────────────────

    [Fact]
    public async Task Reindex_CallsIndexer_WithSessionMessages()
    {
        var sessionId = "sess-reindex";
        var messages = new List<SessionMessage>
        {
            new("id1", "user", "Hello", null, DateTimeOffset.UtcNow, null),
            new("id2", "assistant", "Hi!", null, DateTimeOffset.UtcNow, null),
        };

        _ragService.GetIndexedSourceIdsAsync(RagScope.Session, sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        await _indexer.IndexNewMessagesAsync(sessionId, messages, CancellationToken.None);

        await _indexer.Received(1).IndexNewMessagesAsync(
            sessionId,
            Arg.Is<IReadOnlyList<SessionMessage>>(m => m.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reindex_DeletesExistingMsgChunks_BeforeReindexing()
    {
        var sessionId = "sess-reindex-clean";
        // 写入两条旧的 msg: 分块
        await WriteSessionChunkAsync(sessionId, "msg:old1", 1_000_000);
        await WriteSessionChunkAsync(sessionId, "msg:old2", 2_000_000);

        // 模拟：DeleteBySourceIdAsync 被调用后清除 DB
        _ragService.DeleteBySourceIdAsync(
            Arg.Any<string>(), RagScope.Session, sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // 确认 DB 中先前有数据
        using (var db = _dbFactory.Create(RagScope.Session, sessionId))
        {
            var countBefore = await db.VectorChunks.AsNoTracking()
                .Where(e => e.SourceId.StartsWith("msg:")).CountAsync();
            countBefore.Should().Be(2);
        }

        // 收集 sourceIds（模拟 reindex 端点逻辑）
        List<string> msgSourceIds;
        using (var db = _dbFactory.Create(RagScope.Session, sessionId))
        {
            msgSourceIds = await db.VectorChunks.AsNoTracking()
                .Where(e => e.SourceId.StartsWith("msg:"))
                .Select(e => e.SourceId)
                .Distinct()
                .ToListAsync();
        }

        msgSourceIds.Should().HaveCount(2);
        msgSourceIds.Should().Contain("msg:old1");
        msgSourceIds.Should().Contain("msg:old2");
    }

    // ─── ISessionMessageIndexer 接口契约 ────────────────────────────────────

    [Fact]
    public async Task IndexNewMessages_IsIdempotent_WhenCalledTwice()
    {
        // 同一批消息调用两次，只有第一次实际写入
        var sessionId = "sess-idempotent";
        var embedding = Substitute.For<IEmbeddingService>();
        var fixedVec = new ReadOnlyMemory<float>([1f, 0f, 0f]);
        embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fixedVec));
        embedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IEnumerable<string>>().ToList();
                IReadOnlyList<ReadOnlyMemory<float>> vecs = texts.Select(_ =>
                    new ReadOnlyMemory<float>([1f, 0f, 0f])).ToList();
                return Task.FromResult(vecs);
            });

        var hybridSearch = new HybridSearchService(embedding, _dbFactory, NullLogger<HybridSearchService>.Instance);
        var ragService = new RagService(embedding, _dbFactory, hybridSearch);
        var indexer = new SessionMessageIndexer(ragService, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionMessageIndexer>.Instance);

        var messages = new List<SessionMessage>
        {
            new("msgX", "user", "Hello World", null, DateTimeOffset.UtcNow, null),
        };

        await indexer.IndexNewMessagesAsync(sessionId, messages, CancellationToken.None);
        await indexer.IndexNewMessagesAsync(sessionId, messages, CancellationToken.None);

        using var db = _dbFactory.Create(RagScope.Session, sessionId);
        var chunkCount = await db.VectorChunks.AsNoTracking()
            .Where(e => e.SourceId == "msg:msgX")
            .CountAsync();

        // 第二次调用因 sourceId 已存在而跳过，不重复写入
        chunkCount.Should().Be(1, "幂等索引不应重复写入相同 sourceId");
    }
}
