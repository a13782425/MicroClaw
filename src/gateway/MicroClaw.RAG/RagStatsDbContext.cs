using Microsoft.EntityFrameworkCore;

namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索统计数据库上下文，管理 <see cref="RagSearchStatEntity"/> 的持久化。
/// 数据库文件存储于 <c>{workspaceRoot}/ragstats.db</c>。
/// </summary>
public sealed class RagStatsDbContext(DbContextOptions<RagStatsDbContext> options) : DbContext(options)
{
    public DbSet<RagSearchStatEntity> SearchStats => Set<RagSearchStatEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RagSearchStatEntity>(b =>
        {
            b.ToTable("search_stats");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(32);
            b.Property(e => e.ElapsedMs).HasColumnName("elapsed_ms");
            b.Property(e => e.RecallCount).HasColumnName("recall_count");
            b.Property(e => e.RecordedAtMs).HasColumnName("recorded_at_ms");
            b.HasIndex(e => e.RecordedAtMs).HasDatabaseName("ix_search_stats_recorded_at_ms");
            b.HasIndex(e => e.Scope).HasDatabaseName("ix_search_stats_scope");
        });
    }
}
