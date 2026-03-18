using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ProviderConfigEntity> Providers => Set<ProviderConfigEntity>();

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
    }
}
