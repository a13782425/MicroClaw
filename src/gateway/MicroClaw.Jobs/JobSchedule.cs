namespace MicroClaw.Jobs;

/// <summary>系统后台 Job 的调度策略。</summary>
public abstract record JobSchedule
{
    private JobSchedule() { }

    /// <summary>固定间隔循环执行。</summary>
    /// <param name="Interval">循环间隔。</param>
    /// <param name="StartupDelay">应用启动后首次执行前的等待时间。</param>
    public sealed record FixedInterval(TimeSpan Interval, TimeSpan StartupDelay) : JobSchedule;

    /// <summary>每天 UTC 指定时刻执行一次。</summary>
    /// <param name="TimeUtc">UTC 执行时刻。</param>
    /// <param name="StartupDelay">应用启动后注册 Job 前的等待时间。</param>
    public sealed record DailyAt(TimeOnly TimeUtc, TimeSpan StartupDelay) : JobSchedule;
}
