using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>定时任务工具提供者，包装 <see cref="CronTools"/>。需要 sessionId，为空时返回空列表。</summary>
public sealed class CronToolProvider(CronJobStore cronJobStore, ICronJobScheduler cronScheduler) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "cron";
    public string DisplayName => "定时任务";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        CronTools.GetToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return Task.FromResult(ToolProviderResult.Empty);
        return Task.FromResult(new ToolProviderResult(CronTools.CreateForSession(context.SessionId, cronJobStore, cronScheduler)));
    }
}
