#if DEBUG
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MicroClaw.Database;

/// <summary>
/// Design-time factory for dotnet-ef migrations. Not used at runtime.
/// </summary>
internal sealed class GlobalDbContextFactory : IDesignTimeDbContextFactory<GlobalDbContext>
{
    public GlobalDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GlobalDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time.db");
        return new GlobalDbContext(optionsBuilder.Options);
    }
}
#endif
