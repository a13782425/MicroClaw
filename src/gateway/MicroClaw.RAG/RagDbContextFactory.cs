using Microsoft.EntityFrameworkCore;

namespace MicroClaw.RAG;

/// <summary>
/// 按 <see cref="RagScope"/> 路由到对应 SQLite 数据库的 <see cref="RagDbContext"/> 工厂。
/// <list type="bullet">
///   <item><see cref="RagScope.Global"/> → <c>{workspaceRoot}/globalrag.db</c></item>
///   <item><see cref="RagScope.Session"/> → <c>{workspaceRoot}/sessions/{sessionId}/rag.db</c></item>
/// </list>
/// 首次访问时自动创建数据库和表结构。
/// </summary>
public sealed class RagDbContextFactory
{
    private readonly string _workspaceRoot;

    public RagDbContextFactory(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>全局文档存储目录：<c>{workspaceRoot}/global_docs</c>。</summary>
    public string GlobalDocsPath => Path.Combine(_workspaceRoot, "global_docs");

    /// <summary>
    /// 创建指定作用域的 <see cref="RagDbContext"/>。
    /// 调用方负责 <c>Dispose</c>（推荐 <c>using</c>）。
    /// </summary>
    /// <param name="scope">RAG 作用域。</param>
    /// <param name="sessionId"><see cref="RagScope.Session"/> 时必须提供会话 ID。</param>
    public RagDbContext Create(RagScope scope, string? sessionId = null)
    {
        string dbPath = ResolveDatabasePath(scope, sessionId);

        string? dir = Path.GetDirectoryName(dbPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new RagDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// 获取指定作用域对应的数据库文件路径（不创建文件）。
    /// </summary>
    public string ResolveDatabasePath(RagScope scope, string? sessionId = null)
    {
        return scope switch
        {
            RagScope.Global => Path.Combine(_workspaceRoot, "globalrag.db"),
            RagScope.Session => ResolveSessionPath(sessionId),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未知的 RagScope 值")
        };
    }

    private string ResolveSessionPath(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session 作用域必须提供 sessionId", nameof(sessionId));

        return Path.Combine(_workspaceRoot, "sessions", sessionId, "rag.db");
    }
}
