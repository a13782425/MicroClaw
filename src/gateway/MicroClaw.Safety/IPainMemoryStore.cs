namespace MicroClaw.Safety;

/// <summary>
/// 痛觉记忆存储接口：负责按 Agent 隔离持久化痛觉记忆，支持 CRUD 操作。
/// </summary>
public interface IPainMemoryStore
{
    /// <summary>
    /// 记录一条新的痛觉记忆。每次调用写入独立的一条记录。
    /// </summary>
    /// <param name="memory">要保存的痛觉记忆。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已保存的记忆（包含生成的 Id 和时间戳）。</returns>
    Task<PainMemory> RecordAsync(PainMemory memory, CancellationToken ct = default);

    /// <summary>
    /// 获取指定 Agent 的所有痛觉记忆，按严重度降序、发生次数降序排列。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<PainMemory>> GetAllAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// 将指定 Id 的痛觉记忆发生次数 +1，并更新最近发生时间。
    /// 若记录不存在或不属于该 Agent，则静默忽略。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符（用于权限校验）。</param>
    /// <param name="painMemoryId">目标记忆的 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的记忆；若不存在则返回 null。</returns>
    Task<PainMemory?> IncrementOccurrenceAsync(string agentId, string painMemoryId, CancellationToken ct = default);

    /// <summary>
    /// 删除指定 Id 的痛觉记忆。若记录不存在或不属于该 Agent，则静默忽略。
    /// </summary>
    /// <param name="agentId">Agent 唯一标识符（用于权限校验）。</param>
    /// <param name="painMemoryId">目标记忆的 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteAsync(string agentId, string painMemoryId, CancellationToken ct = default);
}
