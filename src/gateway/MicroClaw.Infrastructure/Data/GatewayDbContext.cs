using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ProviderConfigEntity> Providers => Set<ProviderConfigEntity>();
    public DbSet<ChannelConfigEntity> Channels => Set<ChannelConfigEntity>();
    public DbSet<AgentConfigEntity> Agents => Set<AgentConfigEntity>();
    public DbSet<CronJobEntity> CronJobs => Set<CronJobEntity>();
    public DbSet<CronJobRunLogEntity> CronJobRunLogs => Set<CronJobRunLogEntity>();
    public DbSet<SkillConfigEntity> Skills => Set<SkillConfigEntity>();
    public DbSet<UsageEntity> Usages => Set<UsageEntity>();
    public DbSet<ChannelRetryQueueEntity> ChannelRetryQueue => Set<ChannelRetryQueueEntity>();
    public DbSet<McpServerConfigEntity> McpServers => Set<McpServerConfigEntity>();
    public DbSet<RagConfigEntity> RagConfigs => Set<RagConfigEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(b =>
        {
            b.ToTable("sessions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Title).HasColumnName("title");
            b.Property(e => e.ProviderId).HasColumnName("provider_id");
            b.Property(e => e.IsApproved).HasColumnName("is_approved");
            b.Property(e => e.ChannelType).HasColumnName("channel_type");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(e => e.AgentId).HasColumnName("agent_id");
            b.Property(e => e.ParentSessionId).HasColumnName("parent_session_id");
            b.Property(e => e.ApprovalReason).HasColumnName("approval_reason");
        });

        modelBuilder.Entity<ProviderConfigEntity>(b =>
        {
            b.ToTable("providers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.DisplayName).HasColumnName("display_name");
            b.Property(e => e.Protocol).HasColumnName("protocol");
            b.Property(e => e.BaseUrl).HasColumnName("base_url");
            b.Property(e => e.ApiKey).HasColumnName("api_key");
            b.Property(e => e.ModelName).HasColumnName("model_name");
            b.Property(e => e.MaxOutputTokens).HasColumnName("max_output_tokens").HasDefaultValue(8192);
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            b.Property(e => e.CapabilitiesJson).HasColumnName("capabilities_json");
        });

        modelBuilder.Entity<ChannelConfigEntity>(b =>
        {
            b.ToTable("channels");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.DisplayName).HasColumnName("display_name");
            b.Property(e => e.ChannelType).HasColumnName("channel_type");
            b.Property(e => e.ProviderId).HasColumnName("provider_id");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.SettingsJson).HasColumnName("settings_json");
        });

        modelBuilder.Entity<AgentConfigEntity>(b =>
        {
            b.ToTable("agents");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.BoundSkillIdsJson).HasColumnName("bound_skill_ids_json");
            b.Property(e => e.EnabledMcpServerIdsJson).HasColumnName("enabled_mcp_server_ids_json");
            b.Property(e => e.ToolGroupConfigsJson).HasColumnName("tool_group_configs_json");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(e => e.IsDefault).HasColumnName("is_default");
            b.Property(e => e.ContextWindowMessages).HasColumnName("context_window_messages");
        });

        modelBuilder.Entity<CronJobEntity>(b =>
        {
            b.ToTable("cron_jobs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.CronExpression).HasColumnName("cron_expression").IsRequired(false);
            b.Property(e => e.RunAtUtc).HasColumnName("run_at_utc");
            b.Property(e => e.TargetSessionId).HasColumnName("target_session_id").HasMaxLength(64);
            b.Property(e => e.Prompt).HasColumnName("prompt");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(e => e.LastRunAtUtc).HasColumnName("last_run_at_utc");
        });

        modelBuilder.Entity<CronJobRunLogEntity>(b =>
        {
            b.ToTable("cron_job_run_logs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.CronJobId).HasColumnName("cron_job_id").HasMaxLength(64);
            b.Property(e => e.TriggeredAtUtc).HasColumnName("triggered_at_utc");
            b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            b.Property(e => e.DurationMs).HasColumnName("duration_ms");
            b.Property(e => e.ErrorMessage).HasColumnName("error_message");
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(20);
            b.HasIndex(e => e.CronJobId).HasDatabaseName("ix_cron_job_run_logs_cron_job_id");
        });

        modelBuilder.Entity<SkillConfigEntity>(b =>
        {
            b.ToTable("skills");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<UsageEntity>(b =>
        {
            b.ToTable("usages");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired(false);
            b.Property(e => e.ProviderId).HasColumnName("provider_id").HasMaxLength(64);
            b.Property(e => e.ProviderName).HasColumnName("provider_name");
            b.Property(e => e.Source).HasColumnName("source").HasMaxLength(32);
            b.Property(e => e.InputTokens).HasColumnName("input_tokens");
            b.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            b.Property(e => e.InputPricePerMToken).HasColumnName("input_price_per_m_token").IsRequired(false);
            b.Property(e => e.OutputPricePerMToken).HasColumnName("output_price_per_m_token").IsRequired(false);
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.HasIndex(e => e.CreatedAtUtc).HasDatabaseName("ix_usages_created_at_utc");
        });

        // F-D-1: 渠道消息失败重试队列
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
            b.Property(e => e.NextRetryAt).HasColumnName("next_retry_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.LastErrorMessage).HasColumnName("last_error_message");
            b.HasIndex(e => e.Status).HasDatabaseName("ix_channel_retry_queue_status");
            b.HasIndex(e => e.MessageId).HasDatabaseName("ix_channel_retry_queue_message_id").IsUnique();
        });

        modelBuilder.Entity<McpServerConfigEntity>(b =>
        {
            b.ToTable("mcp_server_configs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.TransportType).HasColumnName("transport_type").HasMaxLength(16);
            b.Property(e => e.Command).HasColumnName("command").IsRequired(false);
            b.Property(e => e.ArgsJson).HasColumnName("args_json").IsRequired(false);
            b.Property(e => e.EnvJson).HasColumnName("env_json").IsRequired(false);
            b.Property(e => e.Url).HasColumnName("url").IsRequired(false);
            b.Property(e => e.HeadersJson).HasColumnName("headers_json").IsRequired(false);
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<RagConfigEntity>(b =>
        {
            b.ToTable("rag_configs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(16);
            b.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired(false);
            b.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(64);
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.HasIndex(e => e.Scope).HasDatabaseName("ix_rag_configs_scope");
        });
    }
}
