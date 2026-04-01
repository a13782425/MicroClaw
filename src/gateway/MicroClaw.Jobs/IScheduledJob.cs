namespace MicroClaw.Jobs;

/// <summary>
/// 系统后台调度 Job 接口。
/// 实现此接口并通过 DI 注册为 <see cref="IScheduledJob"/>，
/// <see cref="SystemJobRegistrar"/> 将在启动时自动将其注册到 Quartz 调度器。
/// </summary>
public interface IScheduledJob
{
    /// <summary>Job 唯一名称，作为 Quartz JobKey.Name，同时用于日志标识。</summary>
    string JobName { get; }

    /// <summary>调度策略（固定间隔或每日指定时刻）。</summary>
    JobSchedule Schedule { get; }

    /// <summary>执行 Job 核心业务逻辑。由 <see cref="SystemJobRunner"/> 在调度时调用。</summary>
    Task ExecuteAsync(CancellationToken ct);
}
