using MicroClaw.Pet.Storage;

namespace MicroClaw.Pet.RateLimit;

/// <summary>
/// Pet 滑动窗口速率限制器。
/// <para>
/// 唯一硬限制：当窗口内 LLM 调用次数达到上限时，拒绝所有后续 LLM 调用。
/// 窗口大小和调用上限从每个 Session 的 <see cref="PetConfig"/> 读取（默认 100 次/5 小时）。
/// </para>
/// <para>
/// 窗口滑动逻辑：当当前时间距离窗口起始已超过窗口时长时，自动重置计数器并更新窗口起点。
/// </para>
/// </summary>
public sealed class PetRateLimiter(PetStateStore stateStore)
{
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));

    /// <summary>
    /// 尝试消耗一次 LLM 调用配额。
    /// <para>
    /// 流程：加载 PetState + PetConfig → 检查窗口是否过期（过期则重置）→ 判断是否超限 → 递增计数并持久化。
    /// </para>
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns><c>true</c> 表示允许调用；<c>false</c> 表示超限拒绝。</returns>
    public async Task<bool> TryAcquireAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var state = await _stateStore.LoadAsync(sessionId, ct);
        if (state is null) return false; // Pet 不存在

        var config = await _stateStore.LoadConfigAsync(sessionId, ct);
        config ??= new PetConfig();

        var now = DateTimeOffset.UtcNow;
        var windowDuration = TimeSpan.FromHours(config.WindowHours);

        // 窗口过期，重置
        int currentCount = state.LlmCallCount;
        DateTimeOffset windowStart = state.WindowStart;

        if (now - windowStart >= windowDuration)
        {
            currentCount = 0;
            windowStart = now;
        }

        // 超限拒绝
        if (currentCount >= config.MaxLlmCallsPerWindow)
            return false;

        // 递增并持久化
        var updated = state with
        {
            LlmCallCount = currentCount + 1,
            WindowStart = windowStart,
            UpdatedAt = now,
        };
        await _stateStore.SaveAsync(updated, ct);
        return true;
    }

    /// <summary>
    /// 获取当前速率限制状态（不消耗配额）。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>速率限制快照，包含剩余次数和窗口信息；Pet 不存在时返回 <c>null</c>。</returns>
    public async Task<RateLimitStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var state = await _stateStore.LoadAsync(sessionId, ct);
        if (state is null) return null;

        var config = await _stateStore.LoadConfigAsync(sessionId, ct);
        config ??= new PetConfig();

        var now = DateTimeOffset.UtcNow;
        var windowDuration = TimeSpan.FromHours(config.WindowHours);

        int currentCount = state.LlmCallCount;
        DateTimeOffset windowStart = state.WindowStart;

        if (now - windowStart >= windowDuration)
        {
            currentCount = 0;
            windowStart = now;
        }

        int remaining = Math.Max(0, config.MaxLlmCallsPerWindow - currentCount);
        DateTimeOffset windowEnd = windowStart + windowDuration;

        return new RateLimitStatus(
            MaxCalls: config.MaxLlmCallsPerWindow,
            UsedCalls: currentCount,
            RemainingCalls: remaining,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            IsExhausted: remaining == 0);
    }
}

/// <summary>
/// 速率限制状态快照。
/// </summary>
/// <param name="MaxCalls">窗口内最大调用次数。</param>
/// <param name="UsedCalls">窗口内已使用的调用次数。</param>
/// <param name="RemainingCalls">窗口内剩余调用次数。</param>
/// <param name="WindowStart">当前窗口起始时间。</param>
/// <param name="WindowEnd">当前窗口结束时间。</param>
/// <param name="IsExhausted">是否已耗尽配额。</param>
public sealed record RateLimitStatus(
    int MaxCalls,
    int UsedCalls,
    int RemainingCalls,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    bool IsExhausted);
