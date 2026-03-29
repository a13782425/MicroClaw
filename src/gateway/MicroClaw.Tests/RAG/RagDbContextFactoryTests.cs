using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MicroClaw.RAG;

namespace MicroClaw.Tests.RAG;

public class RagDbContextFactoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _factory;

    public RagDbContextFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "microclaw_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _factory = new RagDbContextFactory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 测试清理，忽略 */ }
    }

    // ── 构造函数验证 ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_Invalid_WorkspaceRoot(string? root)
    {
        var act = () => new RagDbContextFactory(root!);
        act.Should().Throw<ArgumentException>();
    }

    // ──路径解析 ──

    [Fact]
    public void ResolveDatabasePath_Global_Returns_GlobalRagDb()
    {
        string path = _factory.ResolveDatabasePath(RagScope.Global);
        path.Should().Be(Path.Combine(_tempDir, "globalrag.db"));
    }

    [Fact]
    public void ResolveDatabasePath_Session_Returns_SessionRagDb()
    {
        string path = _factory.ResolveDatabasePath(RagScope.Session, "abc123");
        path.Should().Be(Path.Combine(_tempDir, "sessions", "abc123", "rag.db"));
    }

    [Fact]
    public void ResolveDatabasePath_Session_Without_SessionId_Throws()
    {
        var act = () => _factory.ResolveDatabasePath(RagScope.Session);
        act.Should().Throw<ArgumentException>().WithMessage("*sessionId*");
    }

    // ── 数据库创建（Global） ──

    [Fact]
    public void Create_Global_Creates_Database_File()
    {
        using var db = _factory.Create(RagScope.Global);
        string dbPath = Path.Combine(_tempDir, "globalrag.db");
        File.Exists(dbPath).Should().BeTrue();
    }

    [Fact]
    public void Create_Global_Has_VectorChunks_Table()
    {
        using var db = _factory.Create(RagScope.Global);
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='vector_chunks'";
        var result = cmd.ExecuteScalar() as string;
        result.Should().Be("vector_chunks");
    }

    // ── 数据库创建（Session） ──

    [Fact]
    public void Create_Session_Creates_Database_In_Session_Directory()
    {
        using var db = _factory.Create(RagScope.Session, "sess-001");
        string dbPath = Path.Combine(_tempDir, "sessions", "sess-001", "rag.db");
        File.Exists(dbPath).Should().BeTrue();
    }

    [Fact]
    public void Create_Session_Without_SessionId_Throws()
    {
        var act = () => _factory.Create(RagScope.Session);
        act.Should().Throw<ArgumentException>();
    }

    // ── CRUD 操作 ──

    [Fact]
    public void Can_Insert_And_Query_VectorChunkEntity()
    {
        var chunk = new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = "doc-1",
            Content = "MicroClaw 是一个微服务网关项目",
            VectorBlob = BitConverter.GetBytes(1.0f), // 简化的单维向量
            MetadataJson = """{"fileName":"readme.md","chunkIndex":0}""",
            CreatedAtMs = 1000
        };

        // 写入
        using (var db = _factory.Create(RagScope.Global))
        {
            db.VectorChunks.Add(chunk);
            db.SaveChanges();
        }

        // 读取
        using (var db = _factory.Create(RagScope.Global))
        {
            var loaded = db.VectorChunks.Find(chunk.Id);
            loaded.Should().NotBeNull();
            loaded!.SourceId.Should().Be("doc-1");
            loaded.Content.Should().Be("MicroClaw 是一个微服务网关项目");
            loaded.MetadataJson.Should().Contain("readme.md");
            loaded.CreatedAtMs.Should().Be(1000);
        }
    }

    [Fact]
    public void Can_Query_By_SourceId_Index()
    {
        using var db = _factory.Create(RagScope.Global);

        db.VectorChunks.AddRange(
            new VectorChunkEntity { Id = "c1", SourceId = "src-A", Content = "chunk 1", VectorBlob = [], CreatedAtMs = 1 },
            new VectorChunkEntity { Id = "c2", SourceId = "src-A", Content = "chunk 2", VectorBlob = [], CreatedAtMs = 2 },
            new VectorChunkEntity { Id = "c3", SourceId = "src-B", Content = "chunk 3", VectorBlob = [], CreatedAtMs = 3 }
        );
        db.SaveChanges();

        var srcA = db.VectorChunks.Where(c => c.SourceId == "src-A").ToList();
        srcA.Should().HaveCount(2);
    }

    [Fact]
    public void Can_Delete_Chunks_By_SourceId()
    {
        using var db = _factory.Create(RagScope.Global);

        db.VectorChunks.AddRange(
            new VectorChunkEntity { Id = "d1", SourceId = "del-src", Content = "to delete 1", VectorBlob = [], CreatedAtMs = 1 },
            new VectorChunkEntity { Id = "d2", SourceId = "del-src", Content = "to delete 2", VectorBlob = [], CreatedAtMs = 2 },
            new VectorChunkEntity { Id = "d3", SourceId = "keep-src", Content = "keep", VectorBlob = [], CreatedAtMs = 3 }
        );
        db.SaveChanges();

        db.VectorChunks.Where(c => c.SourceId == "del-src").ExecuteDelete();

        db.VectorChunks.Count().Should().Be(1);
        db.VectorChunks.Single().SourceId.Should().Be("keep-src");
    }

    [Fact]
    public void VectorBlob_Roundtrip_Preserves_Float_Array()
    {
        float[] original = [0.1f, 0.2f, 0.3f, -0.5f, 1.0f];
        byte[] blob = new byte[original.Length * sizeof(float)];
        Buffer.BlockCopy(original, 0, blob, 0, blob.Length);

        using (var db = _factory.Create(RagScope.Global))
        {
            db.VectorChunks.Add(new VectorChunkEntity
            {
                Id = "vec-1",
                SourceId = "embedding-test",
                Content = "test vector roundtrip",
                VectorBlob = blob,
                CreatedAtMs = 100
            });
            db.SaveChanges();
        }

        using (var db = _factory.Create(RagScope.Global))
        {
            var loaded = db.VectorChunks.Find("vec-1");
            loaded.Should().NotBeNull();

            float[] restored = new float[loaded!.VectorBlob.Length / sizeof(float)];
            Buffer.BlockCopy(loaded.VectorBlob, 0, restored, 0, loaded.VectorBlob.Length);
            restored.Should().BeEquivalentTo(original);
        }
    }

    // ── 隔离性 ──

    [Fact]
    public void Global_And_Session_Databases_Are_Isolated()
    {
        using (var globalDb = _factory.Create(RagScope.Global))
        {
            globalDb.VectorChunks.Add(new VectorChunkEntity
                { Id = "g1", SourceId = "global", Content = "global chunk", VectorBlob = [], CreatedAtMs = 1 });
            globalDb.SaveChanges();
        }

        using (var sessionDb = _factory.Create(RagScope.Session, "sess-iso"))
        {
            sessionDb.VectorChunks.Add(new VectorChunkEntity
                { Id = "s1", SourceId = "session", Content = "session chunk", VectorBlob = [], CreatedAtMs = 2 });
            sessionDb.SaveChanges();
        }

        // 各自只能看到自己的数据
        using (var globalDb = _factory.Create(RagScope.Global))
        {
            globalDb.VectorChunks.Count().Should().Be(1);
            globalDb.VectorChunks.Single().SourceId.Should().Be("global");
        }

        using (var sessionDb = _factory.Create(RagScope.Session, "sess-iso"))
        {
            sessionDb.VectorChunks.Count().Should().Be(1);
            sessionDb.VectorChunks.Single().SourceId.Should().Be("session");
        }
    }

    [Fact]
    public void Different_Sessions_Are_Isolated()
    {
        using (var db1 = _factory.Create(RagScope.Session, "sess-1"))
        {
            db1.VectorChunks.Add(new VectorChunkEntity
                { Id = "x1", SourceId = "s1", Content = "session 1", VectorBlob = [], CreatedAtMs = 1 });
            db1.SaveChanges();
        }

        using (var db2 = _factory.Create(RagScope.Session, "sess-2"))
        {
            db2.VectorChunks.Add(new VectorChunkEntity
                { Id = "x2", SourceId = "s2", Content = "session 2", VectorBlob = [], CreatedAtMs = 2 });
            db2.SaveChanges();
        }

        using var db1Check = _factory.Create(RagScope.Session, "sess-1");
        db1Check.VectorChunks.Count().Should().Be(1);
        db1Check.VectorChunks.Single().Id.Should().Be("x1");

        using var db2Check = _factory.Create(RagScope.Session, "sess-2");
        db2Check.VectorChunks.Count().Should().Be(1);
        db2Check.VectorChunks.Single().Id.Should().Be("x2");
    }
}
