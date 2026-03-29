using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Emotion;

/// <summary>
/// 情绪数据库上下文，管理 <see cref="EmotionSnapshotEntity"/> 的持久化。
/// 对应数据库文件：<c>{workspaceRoot}/emotion.db</c>。
/// </summary>
public sealed class EmotionDbContext(DbContextOptions<EmotionDbContext> options) : DbContext(options)
{
    public DbSet<EmotionSnapshotEntity> EmotionSnapshots => Set<EmotionSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmotionSnapshotEntity>(b =>
        {
            b.ToTable("emotion_snapshots");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(128);
            b.Property(e => e.Alertness).HasColumnName("alertness");
            b.Property(e => e.Mood).HasColumnName("mood");
            b.Property(e => e.Curiosity).HasColumnName("curiosity");
            b.Property(e => e.Confidence).HasColumnName("confidence");
            b.Property(e => e.RecordedAtMs).HasColumnName("recorded_at_ms");
            b.HasIndex(e => e.AgentId).HasDatabaseName("ix_emotion_snapshots_agent_id");
            b.HasIndex(e => new { e.AgentId, e.RecordedAtMs })
                .HasDatabaseName("ix_emotion_snapshots_agent_recorded");
        });
    }
}
