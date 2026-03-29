using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.RAG;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

public class RagServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _factory;
    private readonly IEmbeddingService _embedding;
    private readonly HybridSearchService _hybridSearch;

    public RagServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mc_ragservice_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _factory = new RagDbContextFactory(_tempDir);
        _embedding = Substitute.For<IEmbeddingService>();

        // 默认：任何文本返回固定向量 [1, 0, 0]
        var fixedVec = new ReadOnlyMemory<float>([1f, 0f, 0f]);
        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fixedVec));
        _embedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IEnumerable<string>>().ToList();
                IReadOnlyList<ReadOnlyMemory<float>> vecs = texts
                    .Select(_ => new ReadOnlyMemory<float>([1f, 0f, 0f]))
                    .ToList();
                return Task.FromResult(vecs);
            });

        _hybridSearch = new HybridSearchService(_embedding, _factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RagService CreateSut() => new(_embedding, _factory, _hybridSearch);

    // ── 构造参数校验 ──

    [Fact]
    public void Ctor_NullEmbedding_Throws()
    {
        var act = () => new RagService(null!, _factory, _hybridSearch);
        act.Should().Throw<ArgumentNullException>().WithParameterName("embedding");
    }

    [Fact]
    public void Ctor_NullDbFactory_Throws()
    {
        var act = () => new RagService(_embedding, null!, _hybridSearch);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbFactory");
    }

    [Fact]
    public void Ctor_NullHybridSearch_Throws()
    {
        var act = () => new RagService(_embedding, _factory, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("hybridSearch");
    }

    // ── IngestAsync ──

    [Fact]
    public async Task IngestAsync_EmptyContent_DoesNothing()
    {
        var sut = CreateSut();
        await sut.IngestAsync("   ", RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_PlainText_StoresChunks()
    {
        var content = string.Join(" ", Enumerable.Repeat("hello world", 20)); // 短文本 → 1 块
        var sut = CreateSut();

        await sut.IngestAsync(content, RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Should().NotBeEmpty();
        db.VectorChunks.First().VectorBlob.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IngestAsync_PlainText_AllChunksShareSameSourceId()
    {
        // 生成足够长的内容以产生多个分块
        var content = string.Join(" ", Enumerable.Repeat("word", 1500));
        var sut = CreateSut();

        await sut.IngestAsync(content, RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        var chunks = db.VectorChunks.ToList();
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Select(c => c.SourceId).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task IngestAsync_Markdown_UsesMarkdownChunking()
    {
        var markdown = "# Title\n\nParagraph one.\n\n## Section\n\nParagraph two.";
        var sut = CreateSut();

        await sut.IngestAsync(markdown, RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IngestAsync_Session_StoresInSessionDb()
    {
        const string sessionId = "sess-001";
        var sut = CreateSut();

        await sut.IngestAsync("session specific content", RagScope.Session, sessionId);

        using var db = _factory.Create(RagScope.Session, sessionId);
        db.VectorChunks.Should().NotBeEmpty();

        // 全局库不受影响
        using var globalDb = _factory.Create(RagScope.Global);
        globalDb.VectorChunks.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_MultipleCallsCreateIndependentSourceIds()
    {
        var sut = CreateSut();

        await sut.IngestAsync("first document content", RagScope.Global, null);
        await sut.IngestAsync("second document content", RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        var sourceIds = db.VectorChunks.Select(c => c.SourceId).Distinct().ToList();
        sourceIds.Should().HaveCount(2);
    }

    // ── QueryAsync ──

    [Fact]
    public async Task QueryAsync_EmptyDb_ReturnsEmptyString()
    {
        var sut = CreateSut();
        var result = await sut.QueryAsync("what is AI?", RagScope.Global, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_GlobalScope_ReturnsRelevantContent()
    {
        var sut = CreateSut();
        await sut.IngestAsync("Machine learning is a branch of AI", RagScope.Global, null);

        var result = await sut.QueryAsync("what is machine learning?", RagScope.Global, null);

        result.Should().NotBeEmpty();
        result.Should().Contain("Machine learning");
    }

    [Fact]
    public async Task QueryAsync_WhitespaceQuery_Throws()
    {
        var sut = CreateSut();
        var act = async () => await sut.QueryAsync("   ", RagScope.Global, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task QueryAsync_SessionScope_MergesGlobalAndSessionResults()
    {
        const string sessionId = "sess-merge-001";
        var sut = CreateSut();

        await sut.IngestAsync("global knowledge about planets", RagScope.Global, null);
        await sut.IngestAsync("session note about Mars exploration", RagScope.Session, sessionId);

        var result = await sut.QueryAsync("planets Mars", RagScope.Session, sessionId);

        // 结果应包含来自两个库的内容（通过 \n---\n 分隔）
        result.Should().NotBeEmpty();
        result.Should().Contain("global knowledge about planets");
        result.Should().Contain("session note about Mars exploration");
    }

    [Fact]
    public async Task QueryAsync_SessionScope_NoSessionId_QueriesOnlyGlobal()
    {
        var sut = CreateSut();
        await sut.IngestAsync("global content only", RagScope.Global, null);

        // sessionId 为 null → 仅查全局库，不应抛出
        var result = await sut.QueryAsync("global content", RagScope.Session, null);
        result.Should().Contain("global content only");
    }

    [Fact]
    public async Task QueryAsync_ResultsSeparatedBySeparator()
    {
        // 生成足够内容以产生多个分块
        var sut = CreateSut();
        var content = string.Join(" ", Enumerable.Repeat("knowledge fact", 1500));
        await sut.IngestAsync(content, RagScope.Global, null);

        var result = await sut.QueryAsync("knowledge fact", RagScope.Global, null);

        // 多分块时应有分隔符
        if (result.Contains('\n'))
            result.Should().Contain("---");
    }

    // ── IngestAsync(source, sourceId, ...) ──

    [Fact]
    public async Task IngestWithSourceId_EmptyContent_DoesNothing()
    {
        var sut = CreateSut();
        await sut.IngestAsync("   ", "fixed-id", RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWithSourceId_StoresChunksWithGivenSourceId()
    {
        var sut = CreateSut();
        await sut.IngestAsync("hello world content", "my-source-id", RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Should().NotBeEmpty();
        db.VectorChunks.All(c => c.SourceId == "my-source-id").Should().BeTrue();
    }

    [Fact]
    public async Task IngestWithSourceId_Idempotent_DoesNotDuplicate()
    {
        var sut = CreateSut();
        await sut.IngestAsync("some content", "fixed-id-001", RagScope.Global, null);
        await sut.IngestAsync("some content", "fixed-id-001", RagScope.Global, null); // 重复调用

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.Select(c => c.SourceId).Distinct().Should().ContainSingle();
        db.VectorChunks.Should().HaveCount(1); // 幂等：不应有重复
    }

    [Fact]
    public async Task IngestWithSourceId_EmptySourceId_Throws()
    {
        var sut = CreateSut();
        var act = async () => await sut.IngestAsync("content", "  ", RagScope.Global, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── GetIndexedSourceIdsAsync ──

    [Fact]
    public async Task GetIndexedSourceIds_EmptyDb_ReturnsEmptySet()
    {
        var sut = CreateSut();
        var ids = await sut.GetIndexedSourceIdsAsync(RagScope.Global, null);
        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndexedSourceIds_ReturnsDistinctSourceIds()
    {
        var sut = CreateSut();
        await sut.IngestAsync("doc one", RagScope.Global, null);
        await sut.IngestAsync("doc two", RagScope.Global, null);
        await sut.IngestAsync("msg:abc123", "msg:abc123", RagScope.Global, null);

        var ids = await sut.GetIndexedSourceIdsAsync(RagScope.Global, null);

        ids.Should().HaveCount(3);
        ids.Should().Contain("msg:abc123");
    }

    [Fact]
    public async Task GetIndexedSourceIds_ScopedToCorrectDatabase()
    {
        const string sessionId = "sess-scope-001";
        var sut = CreateSut();
        await sut.IngestAsync("global doc", RagScope.Global, null);
        await sut.IngestAsync("session doc", "msg:sess001", RagScope.Session, sessionId);

        var globalIds = await sut.GetIndexedSourceIdsAsync(RagScope.Global, null);
        var sessionIds = await sut.GetIndexedSourceIdsAsync(RagScope.Session, sessionId);

        globalIds.Should().HaveCount(1);
        globalIds.Should().NotContain("msg:sess001");
        sessionIds.Should().ContainSingle().Which.Should().Be("msg:sess001");
    }

    // ── IngestDocumentAsync ──

    [Fact]
    public async Task IngestDocumentAsync_EmptySource_Throws()
    {
        var sut = CreateSut();
        var act = async () => await sut.IngestDocumentAsync("   ", "test.md", RagScope.Global, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IngestDocumentAsync_EmptyFileName_Throws()
    {
        var sut = CreateSut();
        var act = async () => await sut.IngestDocumentAsync("content", "   ", RagScope.Global, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IngestDocumentAsync_StoresChunksWithDocPrefix()
    {
        var sut = CreateSut();
        var sourceId = await sut.IngestDocumentAsync("hello world document", "guide.md", RagScope.Global, null);

        sourceId.Should().Be("doc:guide.md");

        using var db = _factory.Create(RagScope.Global);
        db.VectorChunks.All(c => c.SourceId == "doc:guide.md").Should().BeTrue();
    }

    [Fact]
    public async Task IngestDocumentAsync_StoresFileNameInMetadata()
    {
        var sut = CreateSut();
        await sut.IngestDocumentAsync("hello world document", "sample.txt", RagScope.Global, null);

        using var db = _factory.Create(RagScope.Global);
        var chunk = db.VectorChunks.First();
        chunk.MetadataJson.Should().Contain("\"filename\"");
        chunk.MetadataJson.Should().Contain("sample.txt");
    }

    [Fact]
    public async Task IngestDocumentAsync_SameFileName_ReplacesOldChunks()
    {
        var sut = CreateSut();
        await sut.IngestDocumentAsync("original content", "guide.md", RagScope.Global, null);

        using (var db1 = _factory.Create(RagScope.Global))
            db1.VectorChunks.Should().NotBeEmpty();

        // 再次上传同名文件（触发重索引）
        await sut.IngestDocumentAsync("updated content version two", "guide.md", RagScope.Global, null);

        using var db2 = _factory.Create(RagScope.Global);
        var chunks = db2.VectorChunks.ToList();
        // 旧分块应被删除，仅保留新分块
        chunks.All(c => c.SourceId == "doc:guide.md").Should().BeTrue();
        // 新内容中应含有新分块（而非原始 "original content"）
        chunks.Any(c => c.Content.Contains("updated")).Should().BeTrue();
        chunks.Any(c => c.Content.Contains("original")).Should().BeFalse();
    }

    // ── DeleteBySourceIdAsync ──

    [Fact]
    public async Task DeleteBySourceId_EmptySourceId_Throws()
    {
        var sut = CreateSut();
        var act = async () => await sut.DeleteBySourceIdAsync("  ", RagScope.Global, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteBySourceId_RemovesAllChunksWithSourceId()
    {
        var sut = CreateSut();
        await sut.IngestDocumentAsync("document content to delete", "delete-me.md", RagScope.Global, null);
        await sut.IngestAsync("keep this content", RagScope.Global, null); // 其他文档不受影响

        using (var db = _factory.Create(RagScope.Global))
            db.VectorChunks.Count(c => c.SourceId == "doc:delete-me.md").Should().BeGreaterThan(0);

        await sut.DeleteBySourceIdAsync("doc:delete-me.md", RagScope.Global, null);

        using var db2 = _factory.Create(RagScope.Global);
        db2.VectorChunks.Where(c => c.SourceId == "doc:delete-me.md").Should().BeEmpty();
        db2.VectorChunks.Should().NotBeEmpty(); // 其他文档仍在
    }

    [Fact]
    public async Task DeleteBySourceId_NonexistentSourceId_DoesNotThrow()
    {
        var sut = CreateSut();
        var act = async () => await sut.DeleteBySourceIdAsync("doc:nonexistent.md", RagScope.Global, null);
        await act.Should().NotThrowAsync();
    }

    // ── ListDocumentsAsync ──

    [Fact]
    public async Task ListDocuments_EmptyDb_ReturnsEmpty()
    {
        var sut = CreateSut();
        var docs = await sut.ListDocumentsAsync(RagScope.Global, null);
        docs.Should().BeEmpty();
    }

    [Fact]
    public async Task ListDocuments_ReturnsOnlyDocPrefixedEntries()
    {
        var sut = CreateSut();
        await sut.IngestDocumentAsync("document text", "readme.md", RagScope.Global, null);
        await sut.IngestAsync("msg content", "msg:abc123", RagScope.Global, null); // 消息不应出现

        var docs = await sut.ListDocumentsAsync(RagScope.Global, null);

        docs.Should().ContainSingle();
        docs[0].SourceId.Should().Be("doc:readme.md");
        docs[0].FileName.Should().Be("readme.md");
    }

    [Fact]
    public async Task ListDocuments_AggregatesChunkCount()
    {
        // 足够长的内容会产生多个分块
        var content = string.Join(" ", Enumerable.Repeat("chunk word", 800));
        var sut = CreateSut();
        await sut.IngestDocumentAsync(content, "big-doc.md", RagScope.Global, null);

        var docs = await sut.ListDocumentsAsync(RagScope.Global, null);

        docs.Should().ContainSingle();
        docs[0].ChunkCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListDocuments_ReturnsMultipleDocumentsSortedByFileName()
    {
        var sut = CreateSut();
        await sut.IngestDocumentAsync("zebra doc", "zebra.md", RagScope.Global, null);
        await sut.IngestDocumentAsync("apple doc", "apple.md", RagScope.Global, null);

        var docs = await sut.ListDocumentsAsync(RagScope.Global, null);

        docs.Should().HaveCount(2);
        docs[0].FileName.Should().Be("apple.md");
        docs[1].FileName.Should().Be("zebra.md");
    }
}
