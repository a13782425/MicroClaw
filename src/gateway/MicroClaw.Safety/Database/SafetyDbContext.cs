using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Safety;

/// <summary>
/// 安全/痛觉数据库上下文，管理 <see cref="PainMemoryEntity"/> 的持久化。
/// 对应数据库文件：<c>{workspaceRoot}/safety.db</c>。
/// </summary>
public sealed class SafetyDbContext(DbContextOptions<SafetyDbContext> options) : DbContext(options)
{
    public DbSet<PainMemoryEntity> PainMemories => Set<PainMemoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PainMemoryEntity>(b =>
        {
            b.ToTable("pain_memories");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(32);
            b.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(128);
            b.Property(e => e.TriggerDescription).HasColumnName("trigger_description");
            b.Property(e => e.ConsequenceDescription).HasColumnName("consequence_description");
            b.Property(e => e.AvoidanceStrategy).HasColumnName("avoidance_strategy");
            b.Property(e => e.Severity).HasColumnName("severity");
            b.Property(e => e.OccurrenceCount).HasColumnName("occurrence_count");
            b.Property(e => e.LastOccurredAtMs).HasColumnName("last_occurred_at_ms");
            b.Property(e => e.CreatedAtMs).HasColumnName("created_at_ms");

            b.HasIndex(e => e.AgentId)
                .HasDatabaseName("ix_pain_memories_agent_id");
            b.HasIndex(e => new { e.AgentId, e.Severity })
                .HasDatabaseName("ix_pain_memories_agent_severity");
        });
    }
}
