using Microsoft.EntityFrameworkCore;

namespace MicroClaw.RAG;

/// <summary>
/// <see cref="RagStatsDbContext"/> 工厂，固定路由到 <c>{workspaceRoot}/ragstats.db</c>。
/// </summary>
public sealed class RagStatsDbContextFactory
{
    private readonly string _workspaceRoot;

    public RagStatsDbContextFactory(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>统计数据库文件路径。</summary>
    public string DbPath => Path.Combine(_workspaceRoot, "ragstats.db");

    /// <summary>
    /// 创建 <see cref="RagStatsDbContext"/>，调用方负责 Dispose（推荐 using）。
    /// </summary>
    public RagStatsDbContext Create()
    {
        string dbPath = DbPath;

        string? dir = Path.GetDirectoryName(dbPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<RagStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new RagStatsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
