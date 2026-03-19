using System.ComponentModel;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Quartz;

namespace MicroClaw.Agent.Tools;

/// <summary>
/// 定时任务 AI 工具工厂：生成可被 AI 调用的 CRUD 函数，用于在对话中管理定时任务。
/// 使用 Microsoft.Extensions.AI 的 AIFunctionFactory（非 MCP），直接注入 DI 服务。
/// </summary>
public static class CronTools
{
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
                        j.CronExpression,
                        j.TargetSessionId,
                        j.Prompt,
                        j.IsEnabled,
                        CreatedAt = j.CreatedAtUtc.ToString("O"),
                        LastRunAt = j.LastRunAtUtc?.ToString("O"),
                    }).ToList();
                },
                name: "list_cron_jobs",
                description: "列出所有定时任务，返回任务ID、名称、Cron表达式、目标会话、触发提示词、启用状态和上次执行时间。"),

            AIFunctionFactory.Create(
                async (
                    [Description("任务名称，简洁描述任务用途")] string name,
                    [Description("Quartz cron 表达式（6位格式：秒 分 时 日 月 周），例如：'0 0 9 * * ?' 表示每天9点，'0 30 8 ? * MON-FRI' 表示工作日8:30。若用户描述的是相对时间（如'5分钟后'），请先调用 get_current_time 获取当前时间再计算绝对 cron 表达式")] string cronExpression,
                    [Description("任务触发时发送给 AI 的提示词，AI 会基于此生成回复")] string prompt,
                    [Description("任务描述（可选）")] string? description = null,
                    [Description("目标会话ID（可选，不填时默认发送到当前会话）")] string? targetSessionId = null) =>
                {
                    if (!CronExpression.IsValidExpression(cronExpression))
                        return (object)new { success = false, error = $"无效的 Cron 表达式：{cronExpression}，请使用 Quartz 6位格式" };

                    string effectiveSessionId = string.IsNullOrWhiteSpace(targetSessionId) ? sessionId : targetSessionId;
                    CronJob job = cronJobStore.Add(name, description, cronExpression, effectiveSessionId, prompt);
                    await cronScheduler.ScheduleJobAsync(job);
                    return new { success = true, job.Id, job.Name, job.CronExpression, TargetSessionId = effectiveSessionId };
                },
                name: "create_cron_job",
                description: "创建新的定时任务。任务将按 Cron 表达式定期触发，向目标会话发送提示词并让 AI 生成回复。"),

            AIFunctionFactory.Create(
                async (
                    [Description("要更新的任务ID")] string id,
                    [Description("新的任务名称（不修改则省略）")] string? name = null,
                    [Description("新的 Quartz cron 表达式（不修改则省略）")] string? cronExpression = null,
                    [Description("新的提示词（不修改则省略）")] string? prompt = null,
                    [Description("新的任务描述（不修改则省略）")] string? description = null,
                    [Description("是否启用（true=启用，false=禁用，不修改则省略）")] bool? isEnabled = null) =>
                {
                    if (cronExpression is not null && !CronExpression.IsValidExpression(cronExpression))
                        return (object)new { success = false, error = $"无效的 Cron 表达式：{cronExpression}" };

                    CronJob? updated = cronJobStore.Update(id, name, description, cronExpression, null, prompt, isEnabled);
                    if (updated is null)
                        return (object)new { success = false, error = $"未找到任务：{id}" };

                    await cronScheduler.RescheduleJobAsync(updated);
                    return new { success = true, updated.Id, updated.Name, updated.CronExpression, updated.IsEnabled };
                },
                name: "update_cron_job",
                description: "更新已有定时任务的配置（名称、Cron表达式、提示词、启用状态等），只需传入要修改的字段。"),

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

            AIFunctionFactory.Create(
                () => new
                {
                    localTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                    utcTime = DateTimeOffset.UtcNow.ToString("O")
                },
                name: "get_current_time",
                description: "获取服务器当前本地时间和 UTC 时间。在需要处理相对时间（如'5分钟后'、'明天上午9点'）时，必须先调用此工具获取当前时间，再计算出正确的 Quartz cron 表达式。"),
        ];
    }
}
