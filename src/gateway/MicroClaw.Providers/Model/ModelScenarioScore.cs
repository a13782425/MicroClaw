namespace MicroClaw.Providers;

/// <summary>
/// 单个场景下的模型评分；用于多模型比较或人工评估，不参与本轮自动路由决策。
/// </summary>
/// <param name="Score">0–100 分；默认 50 表示中等水平。</param>
/// <param name="Notes">可选备注（来源、评测日期等）。</param>
public sealed record ModelScenarioScore(int Score = ModelScenarioScore.DefaultScore, string? Notes = null)
{
    public const int DefaultScore = 50;
    public const int MinScore = 0;
    public const int MaxScore = 100;

    public static ModelScenarioScore Default { get; } = new();

    /// <summary>将分数夹紧到合法区间。</summary>
    public ModelScenarioScore Clamp() =>
        Score < MinScore || Score > MaxScore
            ? this with { Score = Math.Clamp(Score, MinScore, MaxScore) }
            : this;
}
