using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;

namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// Pet 自我感知报告：聚合 Pet 的当前状态快照，供 PetStateMachine 和 PetDecisionEngine 作为 LLM 输入。
/// <para>
/// 包含：Pet 行为状态 + 四维情绪 + 速率限制余量 + Provider 可用情况 + Pet RAG 知识统计 + 系统时间。
/// </para>
/// </summary>
public sealed record PetSelfAwarenessReport
{
    // ── Pet 自身状态 ──

    /// <summary>关联的 Session ID。</summary>
    public required string SessionId { get; init; }

    /// <summary>当前行为状态。</summary>
    public PetBehaviorState BehaviorState { get; init; }

    /// <summary>当前四维情绪状态。</summary>
    public EmotionState EmotionState { get; init; } = EmotionState.Default;

    /// <summary>当前行为模式（由情绪映射）。</summary>
    public BehaviorMode BehaviorMode { get; init; }

    // ── 速率限制 ──

    /// <summary>速率限制状态快照。</summary>
    public RateLimitStatus? RateLimitStatus { get; init; }

    // ── Provider 可用情况 ──

    /// <summary>已启用的 Chat Provider 数量。</summary>
    public int EnabledProviderCount { get; init; }

    /// <summary>已启用的 Chat Provider 摘要列表（Id + DisplayName + 质量/延迟/成本简述）。</summary>
    public IReadOnlyList<ProviderSummary> AvailableProviders { get; init; } = [];

    /// <summary>首选 Provider ID（来自 PetConfig）。</summary>
    public string? PreferredProviderId { get; init; }

    // ── Agent 可用情况 ──

    /// <summary>已启用的 Agent 数量。</summary>
    public int EnabledAgentCount { get; init; }

    /// <summary>已启用的 Agent 摘要列表（Id + Name + Description）。</summary>
    public IReadOnlyList<AgentSummary> AvailableAgents { get; init; } = [];

    // ── RAG 统计 ──

    /// <summary>Pet 私有 RAG 知识库是否存在。</summary>
    public bool HasPetRag { get; init; }

    /// <summary>Pet 私有 RAG 中的文档/分块数量（可选）。</summary>
    public int PetRagChunkCount { get; init; }

    // ── 会话上下文 ──

    /// <summary>会话最近消息摘要（最近 N 条消息的方向+内容片段）。</summary>
    public IReadOnlyList<string> RecentMessageSummaries { get; init; } = [];

    // ── 系统时间 ──

    /// <summary>报告生成时间（UTC）。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Pet 上次心跳时间。</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>Pet 创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Provider 摘要信息，用于 LLM 决策参考。
/// </summary>
public sealed record ProviderSummary(
    string Id,
    string DisplayName,
    string ModelName,
    int QualityScore,
    string LatencyTier,
    decimal? InputPricePerMToken,
    decimal? OutputPricePerMToken,
    bool IsDefault);

/// <summary>
/// Agent 摘要信息，用于 LLM 决策参考。
/// </summary>
public sealed record AgentSummary(
    string Id,
    string Name,
    string Description,
    bool IsDefault);
