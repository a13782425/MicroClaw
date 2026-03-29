using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.RAG;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

public class HybridSearchServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _factory;
    private readonly IEmbeddingService _embedding;

    public HybridSearchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mc_hybrid_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _factory = new RagDbContextFactory(_tempDir);
        _embedding = Substitute.For<IEmbeddingService>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private HybridSearchService CreateSut() => new(_embedding, _factory);

    private static VectorChunkEntity MakeEntity(string id, string content, float[]? vec = null)
    {
        vec ??= [1f, 0f, 0f];
        return new VectorChunkEntity
        {
            Id = id,
            SourceId = "src-1",
            Content = content,
            VectorBlob = VectorHelper.ToBytes(vec),
            MetadataJson = null,
            CreatedAtMs = 1000
        };
    }

    private async Task SeedDataAsync(RagScope scope = RagScope.Global, string? sessionId = null)
    {
        using var db = _factory.Create(scope, sessionId);
        db.VectorChunks.AddRange(
            MakeEntity("e1", "The quick brown fox jumps over the lazy dog", [1f, 0f, 0f]),
            MakeEntity("e2", "Machine learning is a subset of artificial intelligence", [0f, 1f, 0f]),
            MakeEntity("e3", "The fox and the dog became friends", [0.8f, 0.2f, 0f]),
            MakeEntity("e4", "Deep learning neural networks", [0.1f, 0.9f, 0f]),
            MakeEntity("e5", "Natural language processing with transformers", [0.3f, 0.7f, 0f])
        );
        await db.SaveChangesAsync();
    }

    // ── 构造 ──

    [Fact]
    public void Ctor_NullEmbedding_Throws()
    {
        var act = () => new HybridSearchService(null!, _factory);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        var act = () => new HybridSearchService(_embedding, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Tokenize ──

    [Fact]
    public void Tokenize_SplitsByWhitespaceAndPunctuation()
    {
        var tokens = HybridSearchService.Tokenize("Hello, world! This is a test.");
        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
        tokens.Should().Contain("this");
        tokens.Should().Contain("test");
    }

    [Fact]
    public void Tokenize_FiltersSingleCharTokens()
    {
        var tokens = HybridSearchService.Tokenize("I am a test");
        tokens.Should().NotContain("i");
        tokens.Should().NotContain("a");
        tokens.Should().Contain("am");
        tokens.Should().Contain("test");
    }

    [Fact]
    public void Tokenize_DeduplicatesTokens()
    {
        var tokens = HybridSearchService.Tokenize("fox fox FOX");
        tokens.Should().HaveCount(1);
        tokens.Should().Contain("fox");
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        HybridSearchService.Tokenize("").Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_OnlySingleChars_ReturnsEmpty()
    {
        HybridSearchService.Tokenize("a b c").Should().BeEmpty();
    }

    // ── Fuse ──

    [Fact]
    public void Fuse_CombinesSemanticAndKeyword_WithWeights()
    {
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["id1"] = (MakeEntity("id1", "semantic hit"), 0.9f),
            ["id2"] = (MakeEntity("id2", "both hit"), 0.5f)
        };
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["id2"] = (MakeEntity("id2", "both hit"), 1.0f),
            ["id3"] = (MakeEntity("id3", "keyword only"), 0.8f)
        };
        var options = new HybridSearchOptions { SemanticWeight = 0.7f, KeywordWeight = 0.3f, TopK = 10 };

        var results = HybridSearchService.Fuse(semantic, keyword, options);

        results.Should().HaveCount(3);

        // id1: 0.9*0.7 + 0*0.3 = 0.63
        var r1 = results.Single(r => r.Record.Id == "id1");
        r1.FusedScore.Should().BeApproximately(0.63f, 0.001f);

        // id2: 0.5*0.7 + 1.0*0.3 = 0.65
        var r2 = results.Single(r => r.Record.Id == "id2");
        r2.FusedScore.Should().BeApproximately(0.65f, 0.001f);

        // id3: 0*0.7 + 0.8*0.3 = 0.24
        var r3 = results.Single(r => r.Record.Id == "id3");
        r3.FusedScore.Should().BeApproximately(0.24f, 0.001f);

        // 排序：id2 > id1 > id3
        results[0].Record.Id.Should().Be("id2");
        results[1].Record.Id.Should().Be("id1");
        results[2].Record.Id.Should().Be("id3");
    }

    [Fact]
    public void Fuse_RespectsTopK()
    {
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>();
        for (int i = 0; i < 20; i++)
            semantic[$"id{i}"] = (MakeEntity($"id{i}", $"text {i}"), 0.5f);

        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();
        var options = new HybridSearchOptions { TopK = 5 };

        var results = HybridSearchService.Fuse(semantic, keyword, options);
        results.Should().HaveCount(5);
    }

    [Fact]
    public void Fuse_BothEmpty_ReturnsEmpty()
    {
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>();
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();
        var results = HybridSearchService.Fuse(semantic, keyword, new HybridSearchOptions());
        results.Should().BeEmpty();
    }

    [Fact]
    public void Fuse_SemanticOnly_NoKeyword()
    {
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["id1"] = (MakeEntity("id1", "text"), 0.8f)
        };
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();
        var options = new HybridSearchOptions { SemanticWeight = 0.7f, KeywordWeight = 0.3f };

        var results = HybridSearchService.Fuse(semantic, keyword, options);
        results.Should().HaveCount(1);
        results[0].SemanticScore.Should().Be(0.8f);
        results[0].KeywordScore.Should().Be(0f);
        results[0].FusedScore.Should().BeApproximately(0.56f, 0.001f);
    }

    [Fact]
    public void Fuse_KeywordOnly_NoSemantic()
    {
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>();
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["id1"] = (MakeEntity("id1", "keyword text"), 1.0f)
        };
        var options = new HybridSearchOptions { SemanticWeight = 0.7f, KeywordWeight = 0.3f };

        var results = HybridSearchService.Fuse(semantic, keyword, options);
        results.Should().HaveCount(1);
        results[0].FusedScore.Should().BeApproximately(0.3f, 0.001f);
    }

    // ── SearchAsync 集成测试 ──

    [Fact]
    public async Task SearchAsync_NullQuery_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.SearchAsync(null!, RagScope.Global);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.SearchAsync("", RagScope.Global);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_ReturnsHybridResults()
    {
        await SeedDataAsync();

        // Mock embedding: query "fox dog" → vec close to e1/e3
        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0.9f, 0.1f, 0f]));

        var sut = CreateSut();
        var results = await sut.SearchAsync("fox dog", RagScope.Global);

        results.Should().NotBeEmpty();
        // e1 and e3 contain "fox" and/or "dog" → both keyword AND semantic hits
        var ids = results.Select(r => r.Record.Id).ToList();
        ids.Should().Contain("e1"); // "quick brown fox... lazy dog" + high semantic
        ids.Should().Contain("e3"); // "fox and dog" + moderate semantic
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        await SeedDataAsync();

        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0.5f, 0.5f, 0f]));

        var sut = CreateSut();
        var options = new HybridSearchOptions { TopK = 2 };
        var results = await sut.SearchAsync("learning fox", RagScope.Global, options: options);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_CustomWeights()
    {
        await SeedDataAsync();

        // Vec close to e2/e4 (ML/DL topics)
        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0f, 1f, 0f]));

        var sut = CreateSut();

        // Pure keyword weight → should favor keyword matches
        var keywordOnly = new HybridSearchOptions { SemanticWeight = 0f, KeywordWeight = 1f, TopK = 5 };
        var results = await sut.SearchAsync("machine learning", RagScope.Global, options: keywordOnly);

        // e2 ("Machine learning...") should score highest with pure keyword
        if (results.Count > 0)
            results[0].Record.Id.Should().Be("e2");
    }

    [Fact]
    public async Task SearchAsync_EmptyDatabase_ReturnsEmpty()
    {
        // Ensure DB exists but empty
        using (var db = _factory.Create(RagScope.Global))
        { /* EnsureCreated in factory */ }

        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([1f, 0f, 0f]));

        var sut = CreateSut();
        var results = await sut.SearchAsync("anything", RagScope.Global);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_SessionScope_Isolation()
    {
        // Seed data in session scope
        using var db = _factory.Create(RagScope.Session, "sess-1");
        db.VectorChunks.Add(MakeEntity("s1", "session specific data", [1f, 0f, 0f]));
        await db.SaveChangesAsync();

        _embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([1f, 0f, 0f]));

        var sut = CreateSut();

        // Search in session → should find it
        var sessionResults = await sut.SearchAsync("session data", RagScope.Session, "sess-1");
        sessionResults.Should().NotBeEmpty();

        // Search in global → should NOT find session data
        using (var globalDb = _factory.Create(RagScope.Global)) { /* ensure exists */ }
        var globalResults = await sut.SearchAsync("session data", RagScope.Global);
        globalResults.Should().BeEmpty();
    }

    // ── HybridSearchOptions 默认值 ──

    [Fact]
    public void Options_DefaultValues()
    {
        var options = new HybridSearchOptions();
        options.SemanticWeight.Should().Be(0.7f);
        options.KeywordWeight.Should().Be(0.3f);
        options.TopK.Should().Be(10);
        options.SemanticCandidateMultiplier.Should().Be(3);
    }
}
