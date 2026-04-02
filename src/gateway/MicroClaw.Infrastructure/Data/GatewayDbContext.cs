using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    // Configuration entities (agents, providers, channels, mcp_server_configs, workflows, sessions)
    // have been migrated to YAML files. Only runtime/high-frequency tables remain here.
    public DbSet<CronJobEntity> CronJobs => Set<CronJobEntity>();
    public DbSet<CronJobRunLogEntity> CronJobRunLogs => Set<CronJobRunLogEntity>();
    public DbSet<UsageEntity> Usages => Set<UsageEntity>();
    public DbSet<ChannelRetryQueueEntity> ChannelRetryQueue => Set<ChannelRetryQueueEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CronJobEntity>(b =>
        {
            b.ToTable("cron_jobs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.CronExpression).HasColumnName("cron_expression").IsRequired(false);
            b.Property(e => e.RunAtMs).HasColumnName("run_at_ms");
            b.Property(e => e.TargetSessionId).HasColumnName("target_session_id").HasMaxLength(64);
            b.Property(e => e.Prompt).HasColumnName("prompt");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtMs).HasColumnName("created_at_ms");
            b.Property(e => e.LastRunAtMs).HasColumnName("last_run_at_ms");
        });

        modelBuilder.Entity<CronJobRunLogEntity>(b =>
        {
            b.ToTable("cron_job_run_logs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.CronJobId).HasColumnName("cron_job_id").HasMaxLength(64);
            b.Property(e => e.TriggeredAtMs).HasColumnName("triggered_at_ms");
            b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            b.Property(e => e.DurationMs).HasColumnName("duration_ms");
            b.Property(e => e.ErrorMessage).HasColumnName("error_message");
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(20);
            b.HasIndex(e => e.CronJobId).HasDatabaseName("ix_cron_job_run_logs_cron_job_id");
        });

        modelBuilder.Entity<UsageEntity>(b =>
        {
            b.ToTable("usages");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(64).IsRequired(false);
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired(false);
            b.Property(e => e.ProviderId).HasColumnName("provider_id").HasMaxLength(64);
            b.Property(e => e.ProviderName).HasColumnName("provider_name");
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(32);
            b.Property(e => e.InputTokens).HasColumnName("input_tokens");
            b.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            b.Property(e => e.CachedInputTokens).HasColumnName("cached_input_tokens").HasDefaultValue(0L);
            b.Property(e => e.DayNumber).HasColumnName("day_number");
            b.Property(e => e.InputCostUsd).HasColumnName("input_cost_usd");
            b.Property(e => e.OutputCostUsd).HasColumnName("output_cost_usd");
            b.Property(e => e.CacheInputCostUsd).HasColumnName("cache_input_cost_usd");
            b.Property(e => e.CacheOutputCostUsd).HasColumnName("cache_output_cost_usd");
            b.Property(e => e.CreatedAtMs).HasColumnName("created_at_ms");
            b.Property(e => e.UpdatedAtMs).HasColumnName("updated_at_ms");
            b.HasIndex(e => new { e.AgentId, e.SessionId, e.ProviderId, e.Source, e.DayNumber })
                .HasDatabaseName("ix_usages_agent_session_provider_source_day")
                .IsUnique();
        });

        modelBuilder.Entity<ChannelRetryQueueEntity>(b =>
        {
            b.ToTable("channel_retry_queue");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.ChannelType).HasColumnName("channel_type").HasMaxLength(32);
            b.Property(e => e.ChannelId).HasColumnName("channel_id").HasMaxLength(64);
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64);
            b.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(128);
            b.Property(e => e.UserText).HasColumnName("user_text");
            b.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            b.Property(e => e.NextRetryAtMs).HasColumnName("next_retry_at_ms");
            b.Property(e => e.CreatedAtMs).HasColumnName("created_at_ms");
            b.Property(e => e.LastErrorMessage).HasColumnName("last_error_message");
            b.HasIndex(e => e.Status).HasDatabaseName("ix_channel_retry_queue_status");
            b.HasIndex(e => e.MessageId).HasDatabaseName("ix_channel_retry_queue_message_id").IsUnique();
        });
    }
}
