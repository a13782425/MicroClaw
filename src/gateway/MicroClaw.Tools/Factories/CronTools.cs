using System.ComponentModel;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Quartz;

namespace MicroClaw.Tools;

/// <summary>
/// 定时任务 AI 工具工厂：生成可被 AI 调用的 CRUD 函数，用于在对话中管理定时任务。
/// 使用 Microsoft.Extensions.AI 的 AIFunctionFactory（非 MCP），直接注入 DI 服务。
/// </summary>
public static class CronTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("list_cron_jobs",    "列出所有定时任务，返回任务ID、名称、类型（one-time=一次性/recurring=周期性）、触发时间或Cron表达式、目标会话、触发提示词、启用状态和上次执行时间。"),
        ("create_cron_job",   "创建定时任务。一次性任务（如'5分钟后'）用 runAt 参数填绝对时间，当前时间已在系统提示中提供；周期性任务（如'每天9点'）用 cronExpression 填 Quartz cron 表达式。两个参数互斥，只能填一个。"),
        ("update_cron_job",   "更新已有定时任务的配置（名称、Cron表达式、提示词、目标会话、启用状态等），只需传入要修改的字段。注意：一次性任务（runAt 类型）不支持修改触发时间，如需更改请删除后重新创建。"),
        ("delete_cron_job",   "删除指定定时任务，任务将从调度器中移除并永久删除。"),
    ];

    /// <summary>返回所有内置定时工具的元数据（不需要 sessionId）。供工具列表 API 使用。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 为指定 Session 创建定时任务工具列表。
    /// 创建任务时，TargetSessionId 默认为当前 sessionId。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateForSession(
        string sessionId,
        CronJobStore cronJobStore,
        ICronJobScheduler cronScheduler)
    {
        return
        [
            AIFunctionFactory.Create(
                () =>
                {
                    IReadOnlyList<CronJob> jobs = cronJobStore.GetAll();
                    return jobs.Select(j => new
                    {
                        j.Id,
                        j.Name,
                        j.Description,
                        Type = j.RunAtUtc is not null ? "one-time" : "recurring",
                        j.CronExpression,
                        RunAt = j.RunAtUtc?.ToString("O"),
                        j.TargetSessionId,
                        j.Prompt,
                        j.IsEnabled,
                        CreatedAt = j.CreatedAtUtc.ToString("O"),
                        LastRunAt = j.LastRunAtUtc?.ToString("O"),
                    }).ToList();
                },
                name: "list_cron_jobs",
                description: "列出所有定时任务，返回任务ID、名称、类型（one-time=一次性/recurring=周期性）、触发时间或Cron表达式、目标会话、触发提示词、启用状态和上次执行时间。"),

            AIFunctionFactory.Create(
                async (
                    [Description("任务名称，简洁描述任务用途")] string name,
                    [Description("任务触发时发送给 AI 的提示词，AI 会基于此生成回复")] string prompt,
                    [Description(
                        "【一次性任务】预定触发的绝对时间，ISO 8601 格式，例如：'2026-03-20T15:35:00+08:00'。" +
                        "若用户说'5分钟后'、'明天早上9点'等相对时间，请根据系统提示中提供的当前时间，" +
                        "计算出准确的绝对时间后再填入此参数。与 cronExpression 互斥，只能填一个。")]
                    string? runAt = null,
                    [Description(
                        "【周期性任务】Quartz cron 表达式（6位格式：秒 分 时 日 月 周），" +
                        "例如：'0 0 9 * * ?' 表示每天9点，'0 30 8 ? * MON-FRI' 表示工作日8:30。" +
                        "只用于需要重复执行的任务。与 runAt 互斥，只能填一个。")]
                    string? cronExpression = null,
                    [Description("任务描述（可选）")] string? description = null,
                    [Description("目标会话ID（可选，不填时默认发送到当前会话）")] string? targetSessionId = null) =>
                {
                    // 互斥验证
                    bool hasRunAt = !string.IsNullOrWhiteSpace(runAt);
                    bool hasCron = !string.IsNullOrWhiteSpace(cronExpression);

                    if (hasRunAt && hasCron)
                        return (object)new { success = false, error = "runAt 和 cronExpression 不能同时填写，请只选其一：一次性任务用 runAt，周期性任务用 cronExpression。" };
                    if (!hasRunAt && !hasCron)
                        return (object)new { success = false, error = "必须填写 runAt（一次性任务）或 cronExpression（周期性任务）之一。" };

                    DateTimeOffset? runAtUtc = null;
                    if (hasRunAt)
                    {
                        if (!DateTimeOffset.TryParse(runAt, out DateTimeOffset parsed))
                            return (object)new { success = false, error = $"runAt 格式无效：'{runAt}'，请使用 ISO 8601 格式，例如 '2026-03-20T15:35:00+08:00'。" };
                        if (parsed <= DateTimeOffset.UtcNow)
                            return (object)new { success = false, error = $"runAt 指定的时间已过期（{parsed:O}），请指定未来的时间。" };
                        runAtUtc = parsed.ToUniversalTime();
                    }
                    else if (!CronExpression.IsValidExpression(cronExpression!))
                    {
                        return (object)new { success = false, error = $"无效的 Cron 表达式：{cronExpression}，请使用 Quartz 6位格式，例如 '0 0 9 * * ?'。" };
                    }

                    string effectiveSessionId = string.IsNullOrWhiteSpace(targetSessionId) ? sessionId : targetSessionId;
                    CronJob job = cronJobStore.Add(name, description, hasCron ? cronExpression : null, effectiveSessionId, prompt, runAtUtc);
                    try
                    {
                        await cronScheduler.ScheduleJobAsync(job);
                    }
                    catch (ArgumentException ex)
                    {
                        cronJobStore.Delete(job.Id);
                        return (object)new { success = false, error = ex.Message };
                    }

                    return new
                    {
                        success = true,
                        job.Id,
                        job.Name,
                        Type = runAtUtc is not null ? "one-time" : "recurring",
                        RunAt = runAtUtc?.ToString("O"),
                        CronExpression = hasCron ? cronExpression : null,
                        TargetSessionId = effectiveSessionId
                    };
                },
                name: "create_cron_job",
                description: "创建定时任务。一次性任务（如'5分钟后'）用 runAt 参数填绝对时间，当前时间已在系统提示中提供；周期性任务（如'每天9点'）用 cronExpression 填 Quartz cron 表达式。两个参数互斥，只能填一个。"),

            AIFunctionFactory.Create(
                async (
                    [Description("要更新的任务ID")] string id,
                    [Description("新的任务名称（不修改则省略）")] string? name = null,
                    [Description("新的 Quartz cron 表达式（不修改则省略；仅对周期性任务有效）")] string? cronExpression = null,
                    [Description("新的提示词（不修改则省略）")] string? prompt = null,
                    [Description("新的任务描述（不修改则省略）")] string? description = null,
                    [Description("新的目标会话ID（不修改则省略）")] string? targetSessionId = null,
                    [Description("是否启用（true=启用，false=禁用，不修改则省略）")] bool? isEnabled = null) =>
                {
                    if (cronExpression is not null && !CronExpression.IsValidExpression(cronExpression))
                        return (object)new { success = false, error = $"无效的 Cron 表达式：{cronExpression}" };

                    CronJob? updated = cronJobStore.Update(id, name, description, cronExpression, targetSessionId, prompt, isEnabled);
                    if (updated is null)
                        return (object)new { success = false, error = $"未找到任务：{id}" };

                    await cronScheduler.RescheduleJobAsync(updated);
                    return new { success = true, updated.Id, updated.Name, updated.CronExpression, updated.IsEnabled };
                },
                name: "update_cron_job",
                description: "更新已有定时任务的配置（名称、Cron表达式、提示词、目标会话、启用状态等），只需传入要修改的字段。注意：一次性任务（runAt 类型）不支持修改触发时间，如需更改请删除后重新创建。"),

            AIFunctionFactory.Create(
                async ([Description("要删除的任务ID")] string id) =>
                {
                    bool deleted = cronJobStore.Delete(id);
                    if (!deleted)
                        return (object)new { success = false, error = $"未找到任务：{id}" };

                    await cronScheduler.UnscheduleJobAsync(id);
                    return new { success = true, message = $"任务 {id} 已删除" };
                },
                name: "delete_cron_job",
                description: "删除指定定时任务，任务将从调度器中移除并永久删除。"),

        ];
    }
}
