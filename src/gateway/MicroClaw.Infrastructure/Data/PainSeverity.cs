namespace MicroClaw.Infrastructure;

/// <summary>
/// 痛觉记忆的严重度等级，用于决策规避优先级和情绪联动强度。
/// </summary>
public enum PainSeverity
{
    /// <summary>轻微：可接受的小错误，仅记录备查。</summary>
    Low = 0,

    /// <summary>中等：需要注意的问题，应优化规避策略。</summary>
    Medium = 1,

    /// <summary>严重：显著影响任务执行的错误，应主动规避。</summary>
    High = 2,

    /// <summary>致命：可能导致系统损坏或不可逆后果，必须严格拦截。</summary>
    Critical = 3,
}
