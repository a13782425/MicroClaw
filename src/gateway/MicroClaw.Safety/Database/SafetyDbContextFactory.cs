using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Safety;

/// <summary>
/// 创建 <see cref="SafetyDbContext"/> 的工厂，数据库文件固定为
/// <c>{workspaceRoot}/safety.db</c>。首次访问时自动建表。
/// </summary>
public sealed class SafetyDbContextFactory
{
    private readonly string _dbPath;

    public SafetyDbContextFactory(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _dbPath = Path.Combine(workspaceRoot, "safety.db");
    }

    /// <summary>安全数据库文件的绝对路径。</summary>
    public string DbPath => _dbPath;

    /// <summary>
    /// 创建 <see cref="SafetyDbContext"/>。调用方负责 Dispose（推荐 using）。
    /// </summary>
    public SafetyDbContext Create()
    {
        string? dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<SafetyDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        var context = new SafetyDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
