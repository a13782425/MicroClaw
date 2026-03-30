using Microsoft.EntityFrameworkCore;

namespace MicroClaw.RAG;

/// <summary>
/// RAG 向量数据库上下文，管理 <see cref="VectorChunkEntity"/> 的持久化。
/// 每个数据库文件（globalrag.db / sessions/{id}/rag.db）使用独立实例。
/// </summary>
public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<VectorChunkEntity> VectorChunks => Set<VectorChunkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VectorChunkEntity>(b =>
        {
            b.ToTable("vector_chunks");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.SourceId).HasColumnName("source_id").HasMaxLength(256);
            b.Property(e => e.Content).HasColumnName("content");
            b.Property(e => e.VectorBlob).HasColumnName("vector_blob");
            b.Property(e => e.MetadataJson).HasColumnName("metadata_json").IsRequired(false);
            b.Property(e => e.CreatedAtMs).HasColumnName("created_at_ms");
            b.Property(e => e.LastAccessedAtMs).HasColumnName("last_accessed_at_ms").IsRequired(false);
            b.Property(e => e.HitCount).HasColumnName("hit_count").HasDefaultValue(0);
            b.HasIndex(e => e.SourceId).HasDatabaseName("ix_vector_chunks_source_id");
        });
    }
}
