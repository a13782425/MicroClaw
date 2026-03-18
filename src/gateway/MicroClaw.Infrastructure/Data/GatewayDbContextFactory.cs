using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// 设计时工厂，用于 dotnet-ef migrations 命令，无需启动项目。
/// </summary>
public sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GatewayDbContext>();
        optionsBuilder.UseSqlite("Data Source=microclaw.db");
        return new GatewayDbContext(optionsBuilder.Options);
    }
}
