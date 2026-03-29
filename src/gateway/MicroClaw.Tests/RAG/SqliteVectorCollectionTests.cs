using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using MicroClaw.RAG;

namespace MicroClaw.Tests.RAG;

public class SqliteVectorCollectionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _factory;

    public SqliteVectorCollectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mc_vec_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _factory = new RagDbContextFactory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteVectorCollection CreateSut(RagScope scope = RagScope.Global, string? sessionId = null)
        => new("vector_chunks", _factory, scope, sessionId);

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

    // ── 构造 ──

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        var act = () => new SqliteVectorCollection("test", null!, RagScope.Global);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_EmptyName_Throws()
    {
        var act = () => new SqliteVectorCollection("", _factory, RagScope.Global);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Name_Returns_ConstructorValue()
    {
        var sut = CreateSut();
        sut.Name.Should().Be("vector_chunks");
    }

    // ── 集合管理 ──

    [Fact]
    public async Task CollectionExistsAsync_After_EnsureCreated_Returns_True()
    {
        var sut = CreateSut();
        await sut.EnsureCollectionExistsAsync();
        (await sut.CollectionExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCollectionDeletedAsync_Drops_Table()
    {
        var sut = CreateSut();
        await sut.EnsureCollectionExistsAsync();
        await sut.UpsertAsync(MakeEntity("e1", "text"));

        await sut.EnsureCollectionDeletedAsync();

        // 重新创建后应无数据
        await sut.EnsureCollectionExistsAsync();
        var result = await sut.GetAsync("e1");
        result.Should().BeNull();
    }

    // ── 单条 CRUD ──

    [Fact]
    public async Task Upsert_And_Get_Single()
    {
        var sut = CreateSut();
        var entity = MakeEntity("id-1", "Hello world");

        var key = await sut.UpsertAsync(entity);
        key.Should().Be("id-1");

        var retrieved = await sut.GetAsync("id-1");
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("Hello world");
        retrieved.SourceId.Should().Be("src-1");
    }

    [Fact]
    public async Task Upsert_Updates_Existing()
    {
        var sut = CreateSut();
        await sut.UpsertAsync(MakeEntity("id-1", "original"));
        await sut.UpsertAsync(MakeEntity("id-1", "updated"));

        var result = await sut.GetAsync("id-1");
        result!.Content.Should().Be("updated");
    }

    [Fact]
    public async Task Get_NonExistent_Returns_Null()
    {
        var sut = CreateSut();
        var result = await sut.GetAsync("no-such-id");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Delete_Removes_Record()
    {
        var sut = CreateSut();
        await sut.UpsertAsync(MakeEntity("id-1", "text"));
        await sut.DeleteAsync("id-1");

        var result = await sut.GetAsync("id-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistent_Does_Not_Throw()
    {
        var sut = CreateSut();
        var act = () => sut.DeleteAsync("no-such-id");
        await act.Should().NotThrowAsync();
    }

    // ── 批量操作 ──

    [Fact]
    public async Task Upsert_Batch()
    {
        var sut = CreateSut();
        var entities = new[]
        {
            MakeEntity("b1", "batch-1"),
            MakeEntity("b2", "batch-2"),
            MakeEntity("b3", "batch-3")
        };

        await sut.UpsertAsync(entities);

        var r1 = await sut.GetAsync("b1");
        var r3 = await sut.GetAsync("b3");
        r1.Should().NotBeNull();
        r3.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_Batch_Returns_Matching_Records()
    {
        var sut = CreateSut();
        await sut.UpsertAsync(MakeEntity("g1", "one"));
        await sut.UpsertAsync(MakeEntity("g2", "two"));
        await sut.UpsertAsync(MakeEntity("g3", "three"));

        var results = new List<VectorChunkEntity>();
        await foreach (var item in sut.GetAsync(new[] { "g1", "g3" }))
            results.Add(item);

        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(["g1", "g3"]);
    }

    [Fact]
    public async Task Delete_Batch()
    {
        var sut = CreateSut();
        await sut.UpsertAsync(MakeEntity("d1", "one"));
        await sut.UpsertAsync(MakeEntity("d2", "two"));
        await sut.UpsertAsync(MakeEntity("d3", "three"));

        await sut.DeleteAsync(new[] { "d1", "d3" });

        (await sut.GetAsync("d1")).Should().BeNull();
        (await sut.GetAsync("d2")).Should().NotBeNull();
        (await sut.GetAsync("d3")).Should().BeNull();
    }

    // ── 过滤查询 ──

    [Fact]
    public async Task GetAsync_Filter_By_SourceId()
    {
        var sut = CreateSut();
        var e1 = MakeEntity("f1", "apple");
        e1.SourceId = "doc-A";
        var e2 = MakeEntity("f2", "banana");
        e2.SourceId = "doc-B";
        var e3 = MakeEntity("f3", "cherry");
        e3.SourceId = "doc-A";

        await sut.UpsertAsync(e1);
        await sut.UpsertAsync(e2);
        await sut.UpsertAsync(e3);

        var results = new List<VectorChunkEntity>();
        await foreach (var item in sut.GetAsync(e => e.SourceId == "doc-A", top: 10))
            results.Add(item);

        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(["f1", "f3"]);
    }

    // ── 向量搜索 ──

    [Fact]
    public async Task SearchAsync_Returns_TopK_By_Cosine_Similarity()
    {
        var sut = CreateSut();

        // 三个向量：query=[1,0,0]，e1=[1,0,0](完全匹配)，e2=[0,1,0](正交)，e3=[0.7,0.7,0](中间)
        await sut.UpsertAsync(MakeEntity("s1", "exact match", [1f, 0f, 0f]));
        await sut.UpsertAsync(MakeEntity("s2", "orthogonal", [0f, 1f, 0f]));
        await sut.UpsertAsync(MakeEntity("s3", "partial", [0.7f, 0.7f, 0f]));

        ReadOnlyMemory<float> query = new float[] { 1f, 0f, 0f };
        var results = new List<VectorSearchResult<VectorChunkEntity>>();
        await foreach (var item in sut.SearchAsync(query, top: 2))
            results.Add(item);

        results.Should().HaveCount(2);
        // 最相似的应该是 s1
        results[0].Record.Id.Should().Be("s1");
        results[0].Score.Should().BeApproximately(1.0f, 1e-4f);

        // 第二应该是 s3（部分匹配）
        results[1].Record.Id.Should().Be("s3");
        results[1].Score!.Value.Should().BeGreaterThan(0f);
    }

    [Fact]
    public async Task SearchAsync_Empty_Collection_Returns_Empty()
    {
        var sut = CreateSut();
        await sut.EnsureCollectionExistsAsync();

        ReadOnlyMemory<float> query = new float[] { 1f, 0f, 0f };
        var results = new List<VectorSearchResult<VectorChunkEntity>>();
        await foreach (var item in sut.SearchAsync(query, top: 5))
            results.Add(item);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_TopK_Limits_Results()
    {
        var sut = CreateSut();
        for (int i = 0; i < 10; i++)
            await sut.UpsertAsync(MakeEntity($"tk{i}", $"text {i}", [1f, 0f, (float)i / 10]));

        ReadOnlyMemory<float> query = new float[] { 1f, 0f, 0f };
        var results = new List<VectorSearchResult<VectorChunkEntity>>();
        await foreach (var item in sut.SearchAsync(query, top: 3))
            results.Add(item);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchAsync_Skips_Empty_VectorBlob()
    {
        var sut = CreateSut();
        var entityNoVec = MakeEntity("nv", "no vector");
        entityNoVec.VectorBlob = [];
        await sut.UpsertAsync(entityNoVec);
        await sut.UpsertAsync(MakeEntity("wv", "with vector", [1f, 0f, 0f]));

        ReadOnlyMemory<float> query = new float[] { 1f, 0f, 0f };
        var results = new List<VectorSearchResult<VectorChunkEntity>>();
        await foreach (var item in sut.SearchAsync(query, top: 10))
            results.Add(item);

        results.Should().HaveCount(1);
        results[0].Record.Id.Should().Be("wv");
    }

    // ── VectorBlob 往返 ──

    [Fact]
    public async Task VectorBlob_RoundTrip()
    {
        var sut = CreateSut();
        float[] vec = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f];
        await sut.UpsertAsync(MakeEntity("vr", "vec roundtrip", vec));

        var retrieved = await sut.GetAsync("vr");
        float[] restored = VectorHelper.ToFloats(retrieved!.VectorBlob);
        restored.Should().Equal(vec);
    }

    // ── Scope 隔离 ──

    [Fact]
    public async Task Global_And_Session_Are_Isolated()
    {
        var global = CreateSut(RagScope.Global);
        var session = CreateSut(RagScope.Session, "sess-1");

        await global.UpsertAsync(MakeEntity("iso-g", "global data"));
        await session.UpsertAsync(MakeEntity("iso-s", "session data"));

        (await global.GetAsync("iso-s")).Should().BeNull("session data should not leak to global");
        (await session.GetAsync("iso-g")).Should().BeNull("global data should not leak to session");
    }

    [Fact]
    public async Task Different_Sessions_Are_Isolated()
    {
        var s1 = CreateSut(RagScope.Session, "sess-A");
        var s2 = CreateSut(RagScope.Session, "sess-B");

        await s1.UpsertAsync(MakeEntity("ss-a", "session A"));
        await s2.UpsertAsync(MakeEntity("ss-b", "session B"));

        (await s1.GetAsync("ss-b")).Should().BeNull();
        (await s2.GetAsync("ss-a")).Should().BeNull();
    }

    // ── 不支持的向量类型 ──

    [Fact]
    public async Task SearchAsync_Unsupported_VectorType_Throws()
    {
        var sut = CreateSut();
        await sut.EnsureCollectionExistsAsync();

        var act = async () =>
        {
            await foreach (var _ in sut.SearchAsync("not a vector", top: 5)) { }
        };

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
