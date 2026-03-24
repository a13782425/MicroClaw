using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>定时任务工具提供者，包装 <see cref="CronTools"/>。需要 sessionId，为空时返回空列表。</summary>
public sealed class CronToolProvider(CronJobStore cronJobStore, ICronJobScheduler cronScheduler) : IBuiltinToolProvider
{
    public string GroupId => "cron";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        CronTools.GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateTools(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return [];
        return CronTools.CreateForSession(sessionId, cronJobStore, cronScheduler);
    }
}
