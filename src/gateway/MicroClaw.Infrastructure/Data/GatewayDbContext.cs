using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ProviderConfigEntity> Providers => Set<ProviderConfigEntity>();
    public DbSet<ChannelConfigEntity> Channels => Set<ChannelConfigEntity>();
    public DbSet<AgentConfigEntity> Agents => Set<AgentConfigEntity>();
    public DbSet<CronJobEntity> CronJobs => Set<CronJobEntity>();

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
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
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
            b.Property(e => e.SystemPrompt).HasColumnName("system_prompt");
            b.Property(e => e.ProviderId).HasColumnName("provider_id");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.BoundChannelIdsJson).HasColumnName("bound_channel_ids_json");
            b.Property(e => e.McpServersJson).HasColumnName("mcp_servers_json");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<CronJobEntity>(b =>
        {
            b.ToTable("cron_jobs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Description).HasColumnName("description");
            b.Property(e => e.CronExpression).HasColumnName("cron_expression");
            b.Property(e => e.TargetSessionId).HasColumnName("target_session_id").HasMaxLength(64);
            b.Property(e => e.Prompt).HasColumnName("prompt");
            b.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            b.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(e => e.LastRunAtUtc).HasColumnName("last_run_at_utc");
        });
    }
}
