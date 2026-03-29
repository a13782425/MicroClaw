using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Emotion;

/// <summary>
/// 创建 <see cref="EmotionDbContext"/> 的工厂，数据库文件固定为
/// <c>{workspaceRoot}/emotion.db</c>。首次访问时自动建表。
/// </summary>
public sealed class EmotionDbContextFactory
{
    private readonly string _dbPath;

    public EmotionDbContextFactory(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _dbPath = Path.Combine(workspaceRoot, "emotion.db");
    }

    /// <summary>情绪数据库文件的绝对路径。</summary>
    public string DbPath => _dbPath;

    /// <summary>
    /// 创建 <see cref="EmotionDbContext"/>。调用方负责 Dispose（推荐 using）。
    /// </summary>
    public EmotionDbContext Create()
    {
        string? dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<EmotionDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        var context = new EmotionDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
