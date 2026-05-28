using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Database;

internal sealed class GlobalDbContext(DbContextOptions<GlobalDbContext> options) : DbContext(options)
{
    public DbSet<TokenDailyEntity> TokenDaily => Set<TokenDailyEntity>();
    public DbSet<CallDailyEntity> CallDaily => Set<CallDailyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenDailyEntity>(b =>
        {
            b.ToTable("token_daily");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.DayNumber).HasColumnName("day_number");
            b.Property(e => e.ProviderId).HasColumnName("provider_id").HasMaxLength(64);
            b.Property(e => e.ProviderName).HasColumnName("provider_name");
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64);
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(32);
            b.Property(e => e.InputTokens).HasColumnName("input_tokens");
            b.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            b.Property(e => e.CachedInputTokens).HasColumnName("cached_input_tokens");
            b.Property(e => e.InputCostUsd).HasColumnName("input_cost_usd");
            b.Property(e => e.OutputCostUsd).HasColumnName("output_cost_usd");
            b.Property(e => e.CacheInputCostUsd).HasColumnName("cache_input_cost_usd");
            b.Property(e => e.CacheOutputCostUsd).HasColumnName("cache_output_cost_usd");
            b.Property(e => e.UpdatedAtMs).HasColumnName("updated_at_ms");
            b.HasIndex(e => new { e.DayNumber, e.ProviderId, e.SessionId, e.Source }).IsUnique()
                .HasDatabaseName("ix_token_daily_day_provider_session_source");
        });

        modelBuilder.Entity<CallDailyEntity>(b =>
        {
            b.ToTable("call_daily");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.DayNumber).HasColumnName("day_number");
            b.Property(e => e.ProviderId).HasColumnName("provider_id").HasMaxLength(64);
            b.Property(e => e.ProviderName).HasColumnName("provider_name");
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64);
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(32);
            b.Property(e => e.CallCount).HasColumnName("call_count");
            b.Property(e => e.SuccessCount).HasColumnName("success_count");
            b.Property(e => e.FailCount).HasColumnName("fail_count");
            b.Property(e => e.UpdatedAtMs).HasColumnName("updated_at_ms");
            b.HasIndex(e => new { e.DayNumber, e.ProviderId, e.SessionId, e.Source }).IsUnique()
                .HasDatabaseName("ix_call_daily_day_provider_session_source");
        });
    }
}
