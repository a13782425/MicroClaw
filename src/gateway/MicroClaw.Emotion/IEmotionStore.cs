namespace MicroClaw.Emotion;

/// <summary>
/// 情绪状态存储接口：负责按 Agent 隔离持久化情绪快照，并支持历史曲线查询。
/// </summary>
public interface IEmotionStore
{
    /// <summary>
    /// 保存当前情绪状态快照。每次调用写入一条历史记录。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符。</param>
    /// <param name="state">要保存的情绪状态。</param>
    /// <param name="ct">取消令牌。</param>
    Task SaveAsync(string agentId, EmotionState state, CancellationToken ct = default);

    /// <summary>
    /// 获取指定 Agent 的最新情绪状态。若无记录，返回 <see cref="EmotionState.Default"/>。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    Task<EmotionState> GetCurrentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// 查询指定 Agent 在时间范围内的情绪历史曲线（按时间升序排列）。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符。</param>
    /// <param name="from">查询起始时间（Unix 毫秒，含）。</param>
    /// <param name="to">查询结束时间（Unix 毫秒，含）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>历史快照列表，按 <c>RecordedAtMs</c> 升序。</returns>
    Task<IReadOnlyList<EmotionSnapshot>> GetHistoryAsync(
        string agentId,
        long from,
        long to,
        CancellationToken ct = default);
}
