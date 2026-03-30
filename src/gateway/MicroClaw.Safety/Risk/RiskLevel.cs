namespace MicroClaw.Safety;

/// <summary>
/// 工具风险等级枚举，从低到高排序，便于比较。
/// 与 <see cref="PainSeverity"/> 保持相同序数以便联动比较。
/// </summary>
public enum RiskLevel
{
    /// <summary>低风险：只读操作（列目录、读文件、获取时间等）。</summary>
    Low = 0,

    /// <summary>中风险：网络请求、定时任务调度等有副作用但影响有限的操作。</summary>
    Medium = 1,

    /// <summary>高风险：写入/修改文件等可逆但影响明显的操作。</summary>
    High = 2,

    /// <summary>严重风险：任意 Shell 命令执行等不可控操作。</summary>
    Critical = 3,
}
