using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

/// <summary>定时任务 EF Core 存储，支持 GetAll / GetById / Add / Update / Delete / UpdateLastRun。</summary>
public sealed class CronJobStore(IDbContextFactory<GatewayDbContext> factory)
{
    public IReadOnlyList<CronJob> GetAll()
    {
        using GatewayDbContext db = factory.CreateDbContext();
        return db.CronJobs
            .OrderByDescending(e => e.CreatedAtMs)
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
            RunAtMs = runAtUtc.HasValue ? TimeBase.ToMs(runAtUtc.Value) : null,
            TargetSessionId = targetSessionId,
            Prompt = prompt,
            IsEnabled = true,
            CreatedAtMs = TimeBase.NowMs(),
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
        entity.LastRunAtMs = TimeBase.ToMs(lastRunAt);
        db.SaveChanges();
    }

    /// <summary>记录一次执行日志；status: success / failed / cancelled；source: cron / manual。</summary>
    public CronJobRunLog AddRunLog(string cronJobId, string status, long durationMs, string? errorMessage, string source = "cron")
    {
        CronJobRunLogEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CronJobId = cronJobId,
            TriggeredAtMs = TimeBase.NowMs(),
            Status = status,
            DurationMs = durationMs,
            ErrorMessage = errorMessage,
            Source = source,
        };
        using GatewayDbContext db = factory.CreateDbContext();
        db.CronJobRunLogs.Add(entity);
        db.SaveChanges();
        return ToLogRecord(entity);
    }

    /// <summary>按时间倒序获取指定任务的最近执行日志。</summary>
    public IReadOnlyList<CronJobRunLog> GetRunLogs(string cronJobId, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 500);
        using GatewayDbContext db = factory.CreateDbContext();
        return db.CronJobRunLogs
            .Where(e => e.CronJobId == cronJobId)
            .OrderByDescending(e => e.TriggeredAtMs)
            .Take(limit)
            .AsEnumerable()
            .Select(ToLogRecord)
            .ToList()
            .AsReadOnly();
    }

    private static CronJob ToRecord(CronJobEntity e) => new(
        e.Id,
        e.Name,
        e.Description,
        e.CronExpression,
        e.TargetSessionId,
        e.Prompt,
        e.IsEnabled,
        TimeBase.FromMs(e.CreatedAtMs),
        e.LastRunAtMs.HasValue ? TimeBase.FromMs(e.LastRunAtMs.Value) : null,
        e.RunAtMs.HasValue ? TimeBase.FromMs(e.RunAtMs.Value) : null);

    private static CronJobRunLog ToLogRecord(CronJobRunLogEntity e) => new(
        e.Id,
        e.CronJobId,
        TimeBase.FromMs(e.TriggeredAtMs),
        e.Status,
        e.DurationMs,
        e.ErrorMessage,
        e.Source);
}
