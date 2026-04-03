using MicroClaw.Pet.Emotion;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 状态聚合模型。记录当前会话 Pet 的完整运行时状态。
/// 实例不可变，所有修改通过 with 表达式产生新实例。
/// </summary>
public sealed record PetState
{
    /// <summary>关联的会话 ID。</summary>
    public required string SessionId { get; init; }

    /// <summary>当前行为状态。</summary>
    public PetBehaviorState BehaviorState { get; init; } = PetBehaviorState.Idle;

    /// <summary>当前情绪状态（四维）。</summary>
    public EmotionState EmotionState { get; init; } = EmotionState.Default;

    /// <summary>当前窗口内已消耗的 LLM 调用次数。</summary>
    public int LlmCallCount { get; init; } = 0;

    /// <summary>当前速率限制窗口的起始时间（UTC）。</summary>
    public DateTimeOffset WindowStart { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最后一次心跳时间（UTC）。</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>Pet 创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最后一次更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
