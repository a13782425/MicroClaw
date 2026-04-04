using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.Pet.Rag;
using MicroClaw.RAG;
using MicroClaw.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetRagScope 单元测试：
/// - Ingest + Query 往返正确
/// - 幂等 IngestAsync（指定 sourceId）
/// - GetChunkCount 准确
/// - DeleteBySourceId 正确删除
/// - GetAllPetSessionIds 扫描正确
/// - DB 不存在时 Query/Count 安全返回
/// </summary>
public sealed class PetRagScopeTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly IEmbeddingService _embedding;
    private readonly PetRagScope _ragScope;

    private const string SessionId = "pet-rag-test-session";

    public PetRagScopeTests()
    {
        _embedding = CreateMockEmbeddingService();
        _ragScope = new PetRagScope(_embedding, _tempDir.Path, NullLogger<PetRagScope>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    [Fact]
    public void GetDatabasePath_ReturnsCorrectPath()
    {
        var path = _ragScope.GetDatabasePath(SessionId);
        path.Should().EndWith(Path.Combine(SessionId, "pet", "knowledge.db"));
    }

    [Fact]
    public async Task IngestAsync_CreatesDbAndStoresChunks()
    {
        await _ragScope.IngestAsync("这是一段测试文本，用于验证 Pet RAG 的注入功能。", SessionId);

        var count = await _ragScope.GetChunkCountAsync(SessionId);
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IngestAsync_SkipsEmptyContent()
    {
        await _ragScope.IngestAsync("", SessionId);
        await _ragScope.IngestAsync("   ", SessionId);

        var count = await _ragScope.GetChunkCountAsync(SessionId);
        count.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_WithSourceId_IsIdempotent()
    {
        const string sourceId = "test-source-1";
        const string content = "重复注入测试内容，应该只存储一次。";

        await _ragScope.IngestAsync(content, SessionId, sourceId);
        await _ragScope.IngestAsync(content, SessionId, sourceId);

        var count = await _ragScope.GetChunkCountAsync(SessionId);
        count.Should().Be(1); // 只存储一次
    }

    [Fact]
    public async Task QueryAsync_ReturnsRelevantContent()
    {
        await _ragScope.IngestAsync("如何编写单元测试是软件开发中的关键技能。", SessionId);

        var result = await _ragScope.QueryAsync("单元测试", SessionId);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("单元测试");
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmpty_WhenNoDb()
    {
        var result = await _ragScope.QueryAsync("any query", "non-existent-session");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmpty_WhenNoChunks()
    {
        // Create DB by ingesting then deleting
        const string sourceId = "temp";
        await _ragScope.IngestAsync("temp content", SessionId, sourceId);
        await _ragScope.DeleteBySourceIdAsync(sourceId, SessionId);

        var result = await _ragScope.QueryAsync("test", SessionId);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChunkCountAsync_ReturnsZero_WhenNoDb()
    {
        var count = await _ragScope.GetChunkCountAsync("non-existent-session");
        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteBySourceIdAsync_RemovesCorrectChunks()
    {
        await _ragScope.IngestAsync("内容 A", SessionId, "source-a");
        await _ragScope.IngestAsync("内容 B", SessionId, "source-b");

        var countBefore = await _ragScope.GetChunkCountAsync(SessionId);
        countBefore.Should().Be(2);

        await _ragScope.DeleteBySourceIdAsync("source-a", SessionId);

        var countAfter = await _ragScope.GetChunkCountAsync(SessionId);
        countAfter.Should().Be(1);
    }

    [Fact]
    public async Task DeleteBySourceIdAsync_NoOp_WhenNoDb()
    {
        // Should not throw
        await _ragScope.DeleteBySourceIdAsync("any", "non-existent-session");
    }

    [Fact]
    public async Task GetAllPetSessionIds_ReturnsCorrectIds()
    {
        // Create pet knowledge.db for two sessions
        await _ragScope.IngestAsync("内容 1", "session-a");
        await _ragScope.IngestAsync("内容 2", "session-b");

        var ids = _ragScope.GetAllPetSessionIds();

        ids.Should().Contain("session-a");
        ids.Should().Contain("session-b");
    }

    [Fact]
    public void GetAllPetSessionIds_ReturnsEmpty_WhenNoPets()
    {
        var ids = _ragScope.GetAllPetSessionIds();
        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_MarkdownContent_UsesMarkdownChunker()
    {
        var markdown = "# 标题\n\n这是一段 Markdown 文本。\n\n## 子标题\n\n更多内容。";
        await _ragScope.IngestAsync(markdown, SessionId);

        var count = await _ragScope.GetChunkCountAsync(SessionId);
        count.Should().BeGreaterThan(0);
    }

    // ── Mock Embedding Service ──────────────────────────────────────────

    private static IEmbeddingService CreateMockEmbeddingService()
    {
        var mock = Substitute.For<IEmbeddingService>();

        // 返回固定维度的随机向量（128 维）
        mock.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var text = callInfo.ArgAt<string>(0);
                return new ReadOnlyMemory<float>(GenerateDeterministicVector(text, 128));
            });

        mock.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.ArgAt<IEnumerable<string>>(0).ToList();
                IReadOnlyList<ReadOnlyMemory<float>> result = texts
                    .Select(t => new ReadOnlyMemory<float>(GenerateDeterministicVector(t, 128)))
                    .ToList();
                return result;
            });

        return mock;
    }

    /// <summary>
    /// 基于文本哈希生成确定性向量，确保相同文本产生相同向量。
    /// </summary>
    private static float[] GenerateDeterministicVector(string text, int dimensions)
    {
        int hash = text.GetHashCode();
        var rng = new Random(hash);
        var vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
            vector[i] = (float)(rng.NextDouble() * 2 - 1);

        // Normalize
        float norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
            for (int i = 0; i < dimensions; i++)
                vector[i] /= norm;

        return vector;
    }
}
