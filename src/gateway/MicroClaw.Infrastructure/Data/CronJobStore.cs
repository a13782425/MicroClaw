using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

/// <summary>定时任务 EF Core 存储，支持 GetAll / GetById / Add / Update / Delete / UpdateLastRun。</summary>
public sealed class CronJobStore(IDbContextFactory<GatewayDbContext> factory)
{
    public IReadOnlyList<CronJob> GetAll()
    {
        using GatewayDbContext db = factory.CreateDbContext();
        return db.CronJobs
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(ToRecord)
            .ToList()
            .AsReadOnly();
    }

    public CronJob? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        CronJobEntity? entity = db.CronJobs.Find(id);
        return entity is null ? null : ToRecord(entity);
    }

    /// <summary>创建定时任务。cronExpression 与 runAtUtc 二选一，不可同时填写。</summary>
    public CronJob Add(string name, string? description, string? cronExpression, string targetSessionId, string prompt, DateTimeOffset? runAtUtc = null)
    {
        CronJobEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description,
            CronExpression = cronExpression,
            RunAtUtc = runAtUtc?.ToString("O"),
            TargetSessionId = targetSessionId,
            Prompt = prompt,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };

        using GatewayDbContext db = factory.CreateDbContext();
        db.CronJobs.Add(entity);
        db.SaveChanges();
        return ToRecord(entity);
    }

    public CronJob? Update(string id, string? name, string? description, string? cronExpression, string? targetSessionId, string? prompt, bool? isEnabled)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        CronJobEntity? entity = db.CronJobs.Find(id);
        if (entity is null) return null;

        if (name is not null) entity.Name = name;
        if (description is not null) entity.Description = description;
        if (cronExpression is not null) entity.CronExpression = cronExpression;
        if (targetSessionId is not null) entity.TargetSessionId = targetSessionId;
        if (prompt is not null) entity.Prompt = prompt;
        if (isEnabled.HasValue) entity.IsEnabled = isEnabled.Value;

        db.SaveChanges();
        return ToRecord(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        CronJobEntity? entity = db.CronJobs.Find(id);
        if (entity is null) return false;
        db.CronJobs.Remove(entity);
        db.SaveChanges();
        return true;
    }

    public void UpdateLastRun(string id, DateTimeOffset lastRunAt)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        CronJobEntity? entity = db.CronJobs.Find(id);
        if (entity is null) return;
        entity.LastRunAtUtc = lastRunAt.ToString("O");
        db.SaveChanges();
    }

    private static CronJob ToRecord(CronJobEntity e) => new(
        e.Id,
        e.Name,
        e.Description,
        e.CronExpression,
        e.TargetSessionId,
        e.Prompt,
        e.IsEnabled,
        DateTimeOffset.TryParse(e.CreatedAtUtc, out DateTimeOffset created) ? created : DateTimeOffset.MinValue,
        string.IsNullOrWhiteSpace(e.LastRunAtUtc) ? null
            : DateTimeOffset.TryParse(e.LastRunAtUtc, out DateTimeOffset last) ? last : null,
        string.IsNullOrWhiteSpace(e.RunAtUtc) ? null
            : DateTimeOffset.TryParse(e.RunAtUtc, out DateTimeOffset runAt) ? runAt : null);
}
