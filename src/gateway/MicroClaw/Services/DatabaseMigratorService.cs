using MicroClaw.Abstractions;
using MicroClaw.Infrastructure.Data;
using MicroClaw.RAG;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

/// <summary>
/// 数据库迁移服务（<see cref="IService.InitOrder"/> = 0）。
/// <para>
/// 在所有其他服务初始化之前执行 EF Core 迁移，确保数据库 schema 与当前模型一致。
/// 负责主库（<c>microclaw.db</c>）迁移。
/// RAG 数据库使用懒初始化（<see cref="RagDbContextFactory"/>），首次访问时自动建表，无需在此处处理。
/// </para>
/// </summary>
public sealed class DatabaseMigratorService : IService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DatabaseMigratorService> _logger;

    public int InitOrder => 0;

    public DatabaseMigratorService(IServiceProvider sp, ILogger<DatabaseMigratorService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("执行主数据库迁移...");
        using var scope = _sp.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
        using var db = dbFactory.CreateDbContext();
        db.Database.Migrate();
        _logger.LogInformation("主数据库迁移完成。");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
