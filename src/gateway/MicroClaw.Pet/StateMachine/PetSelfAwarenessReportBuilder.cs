using MicroClaw.Agent;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;

namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// <see cref="PetSelfAwarenessReport"/> 构建器：从各数据源聚合信息，生成 Pet 自我感知报告。
/// </summary>
public sealed class PetSelfAwarenessReportBuilder(
    PetStateStore stateStore,
    IEmotionStore emotionStore,
    IEmotionBehaviorMapper behaviorMapper,
    PetRateLimiter rateLimiter,
    ProviderConfigStore providerStore,
    AgentStore agentStore)
{
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));
    private readonly IEmotionBehaviorMapper _behaviorMapper = behaviorMapper ?? throw new ArgumentNullException(nameof(behaviorMapper));
    private readonly PetRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly ProviderConfigStore _providerStore = providerStore ?? throw new ArgumentNullException(nameof(providerStore));
    private readonly AgentStore _agentStore = agentStore ?? throw new ArgumentNullException(nameof(agentStore));

    /// <summary>
    /// 构建指定 Session 的自我感知报告。
    /// </summary>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="recentMessageSummaries">最近消息摘要（由调用方提供，避免向此层引入消息存储依赖）。</param>
    /// <param name="petRagChunkCount">Pet RAG 分块数量（由调用方提供，避免向此层引入 RAG 查询依赖）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>自我感知报告；Pet 不存在时返回 <c>null</c>。</returns>
    public async Task<PetSelfAwarenessReport?> BuildAsync(
        string sessionId,
        IReadOnlyList<string>? recentMessageSummaries = null,
        int petRagChunkCount = 0,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var petState = await _stateStore.LoadAsync(sessionId, ct);
        if (petState is null) return null;

        var config = await _stateStore.LoadConfigAsync(sessionId, ct);
        var emotion = await _emotionStore.GetCurrentAsync(sessionId, ct);
        var behaviorProfile = _behaviorMapper.GetProfile(emotion);
        var rateLimitStatus = await _rateLimiter.GetStatusAsync(sessionId, ct);

        // Provider 摘要
        var allProviders = _providerStore.All;
        var chatProviders = allProviders
            .Where(p => p.IsEnabled && p.ModelType != ModelType.Embedding)
            .ToList();

        var providerSummaries = chatProviders.Select(p => new ProviderSummary(
            Id: p.Id,
            DisplayName: p.DisplayName,
            ModelName: p.ModelName,
            QualityScore: p.Capabilities.QualityScore,
            LatencyTier: p.Capabilities.LatencyTier.ToString(),
            InputPricePerMToken: p.Capabilities.InputPricePerMToken,
            OutputPricePerMToken: p.Capabilities.OutputPricePerMToken,
            IsDefault: p.IsDefault
        )).ToList();

        // Agent 摘要
        var allAgents = _agentStore.All;
        var enabledAgents = allAgents.Where(a => a.IsEnabled).ToList();

        // 如果 PetConfig 指定了 AllowedAgentIds，则过滤
        if (config is { AllowedAgentIds.Count: > 0 })
        {
            var allowed = new HashSet<string>(config.AllowedAgentIds);
            enabledAgents = enabledAgents.Where(a => allowed.Contains(a.Id)).ToList();
        }

        var agentSummaries = enabledAgents.Select(a => new AgentSummary(
            Id: a.Id,
            Name: a.Name,
            Description: a.Description,
            IsDefault: a.IsDefault
        )).ToList();

        // Pet RAG 检查
        bool hasPetRag = petRagChunkCount > 0;

        return new PetSelfAwarenessReport
        {
            SessionId = sessionId,
            BehaviorState = petState.BehaviorState,
            EmotionState = emotion,
            BehaviorMode = behaviorProfile.Mode,
            RateLimitStatus = rateLimitStatus,
            EnabledProviderCount = chatProviders.Count,
            AvailableProviders = providerSummaries,
            PreferredProviderId = config?.PreferredProviderId,
            EnabledAgentCount = enabledAgents.Count,
            AvailableAgents = agentSummaries,
            HasPetRag = hasPetRag,
            PetRagChunkCount = petRagChunkCount,
            RecentMessageSummaries = recentMessageSummaries ?? [],
            Timestamp = DateTimeOffset.UtcNow,
            LastHeartbeatAt = petState.LastHeartbeatAt,
            CreatedAt = petState.CreatedAt,
        };
    }
}
