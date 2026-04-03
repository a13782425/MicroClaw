namespace MicroClaw.Pet.Emotion;

/// <summary>
/// Pet 情绪存储接口：基于 Session 隔离持久化情绪快照。
/// </summary>
public interface IEmotionStore
{
    /// <summary>
    /// 保存当前情绪状态快照。每次调用追加一条历史记录到 journal。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="state">要保存的情绪状态。</param>
    /// <param name="ct">取消令牌。</param>
    Task SaveAsync(string sessionId, EmotionState state, CancellationToken ct = default);

    /// <summary>
    /// 获取指定 Session 的最新情绪状态。若无记录，返回 <see cref="EmotionState.Default"/>。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    Task<EmotionState> GetCurrentAsync(string sessionId, CancellationToken ct = default);
}
