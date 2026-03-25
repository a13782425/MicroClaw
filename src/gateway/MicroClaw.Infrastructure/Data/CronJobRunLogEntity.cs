namespace MicroClaw.Infrastructure.Data;

public sealed class CronJobRunLogEntity
{
    public string Id { get; set; } = string.Empty;
    public string CronJobId { get; set; } = string.Empty;
    /// <summary>触发时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long TriggeredAtMs { get; set; }
    /// <summary>执行结果：success / failed / cancelled</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>执行耗时（毫秒）</summary>
    public long DurationMs { get; set; }
    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>触发来源：cron（自动）/ manual（手动）</summary>
    public string Source { get; set; } = "cron";
}
