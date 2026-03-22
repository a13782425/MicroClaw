using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Jobs;
using Quartz;

namespace MicroClaw.Endpoints;

public static class CronEndpoints
{
    public static IEndpointRouteBuilder MapCronEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/cron — 获取所有定时任务
        endpoints.MapGet("/cron", (CronJobStore store) =>
            Results.Ok(store.GetAll()))
            .WithTags("Cron");

        // POST /api/cron — 创建定时任务
        endpoints.MapPost("/cron", async (CreateCronJobRequest req, CronJobStore store, ICronJobScheduler scheduler, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return ApiErrors.BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(req.CronExpression))
                return ApiErrors.BadRequest("CronExpression is required.");
            if (string.IsNullOrWhiteSpace(req.TargetSessionId))
                return ApiErrors.BadRequest("TargetSessionId is required.");
            if (string.IsNullOrWhiteSpace(req.Prompt))
                return ApiErrors.BadRequest("Prompt is required.");
            if (!CronExpression.IsValidExpression(req.CronExpression))
                return ApiErrors.BadRequest($"Invalid Quartz cron expression: '{req.CronExpression}'.");

            CronJob job = store.Add(req.Name.Trim(), req.Description?.Trim(), req.CronExpression.Trim(), req.TargetSessionId.Trim(), req.Prompt.Trim());
            await scheduler.ScheduleJobAsync(job, ct);
            return Results.Ok(job);
        })
        .WithTags("Cron");

        // POST /api/cron/update — 更新定时任务
        endpoints.MapPost("/cron/update", async (UpdateCronJobRequest req, CronJobStore store, ICronJobScheduler scheduler, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");
            if (req.CronExpression is not null && !CronExpression.IsValidExpression(req.CronExpression))
                return ApiErrors.BadRequest($"Invalid Quartz cron expression: '{req.CronExpression}'.");

            CronJob? updated = store.Update(req.Id, req.Name?.Trim(), req.Description?.Trim(), req.CronExpression?.Trim(), req.TargetSessionId?.Trim(), req.Prompt?.Trim(), req.IsEnabled);
            if (updated is null)
                return ApiErrors.NotFound($"CronJob '{req.Id}' not found.");

            await scheduler.RescheduleJobAsync(updated, ct);
            return Results.Ok(updated);
        })
        .WithTags("Cron");

        // POST /api/cron/delete — 删除定时任务
        endpoints.MapPost("/cron/delete", async (DeleteCronJobRequest req, CronJobStore store, ICronJobScheduler scheduler, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return ApiErrors.NotFound($"CronJob '{req.Id}' not found.");

            await scheduler.UnscheduleJobAsync(req.Id, ct);
            return Results.Ok();
        })
        .WithTags("Cron");

        // POST /api/cron/toggle — 启用/禁用定时任务
        endpoints.MapPost("/cron/toggle", async (ToggleCronJobRequest req, CronJobStore store, ICronJobScheduler scheduler, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            CronJob? existing = store.GetById(req.Id);
            if (existing is null)
                return ApiErrors.NotFound($"CronJob '{req.Id}' not found.");

            CronJob? updated = store.Update(req.Id, null, null, null, null, null, !existing.IsEnabled);
            if (updated is null) return Results.NotFound();

            await scheduler.RescheduleJobAsync(updated, ct);
            return Results.Ok(updated);
        })
        .WithTags("Cron");

        // POST /api/cron/trigger — 手动立即触发定时任务并等待结果
        endpoints.MapPost("/cron/trigger", async (TriggerCronJobRequest req, CronJobStore store, SessionChatService chatService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            CronJob? job = store.GetById(req.Id);
            if (job is null)
                return ApiErrors.NotFound($"CronJob '{req.Id}' not found.");

            DateTimeOffset startTime = DateTimeOffset.UtcNow;
            string status = "success";
            string? errorMessage = null;

            try
            {
                await chatService.ExecuteAsync(job.TargetSessionId, job.Prompt, ct);
                store.UpdateLastRun(req.Id, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                status = "cancelled";
                errorMessage = "操作已取消";
                store.UpdateLastRun(req.Id, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                status = "failed";
                errorMessage = ex.Message;
                store.UpdateLastRun(req.Id, DateTimeOffset.UtcNow);
            }

            long durationMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            CronJobRunLog log = store.AddRunLog(req.Id, status, durationMs, errorMessage, source: "manual");

            return Results.Ok(new { success = status == "success", status, durationMs, errorMessage, log });
        })
        .WithTags("Cron");

        // GET /api/cron/{id}/logs — 获取执行历史日志
        endpoints.MapGet("/cron/{id}/logs", (string id, int? limit, CronJobStore store) =>
        {
            CronJob? job = store.GetById(id);
            if (job is null)
                return ApiErrors.NotFound($"CronJob '{id}' not found.");

            IReadOnlyList<CronJobRunLog> logs = store.GetRunLogs(id, limit ?? 50);
            return Results.Ok(logs);
        })
        .WithTags("Cron");

        return endpoints;
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CreateCronJobRequest(
    string? Name,
    string? Description,
    string? CronExpression,
    string? TargetSessionId,
    string? Prompt);

public record UpdateCronJobRequest(
    string? Id,
    string? Name,
    string? Description,
    string? CronExpression,
    string? TargetSessionId,
    string? Prompt,
    bool? IsEnabled);

public record DeleteCronJobRequest(string? Id);

public record ToggleCronJobRequest(string? Id);

public record TriggerCronJobRequest(string? Id);
