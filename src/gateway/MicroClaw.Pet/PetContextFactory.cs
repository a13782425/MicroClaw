using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent;
using MicroClaw.Configuration.Options;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Per-Session <see cref="MicroPet"/> 工厂：从文件系统加载 Pet 状态并构建 <see cref="MicroPet"/> 实例。
/// <para>
/// 本工厂为无状态 Singleton，可多会话并发安全调用。
/// </para>
/// <para>
/// 使用场景：
/// <list type="number">
///   <item>Session 首次审批时，由 <see cref="PetFactory"/> 在创建 Pet 目录后调用。</item>
///   <item>服务重启后，<see cref="PetRunner"/> 会话处理时发现 Session.PetContext 为 null，懒加载。</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetContextFactory(
    PetStateStore stateStore,
    IEmotionStore emotionStore,
    PetDecisionEngine decisionEngine,
    IEmotionRuleEngine emotionRuleEngine,
    IEmotionBehaviorMapper emotionBehaviorMapper,
    PetRagScope petRagScope,
    PetSessionObserver sessionObserver,
    PetRateLimiter rateLimiter,
    PetSelfAwarenessReportBuilder reportBuilder,
    AgentStore agentStore,
    ProviderConfigStore providerStore,
    ILoggerFactory loggerFactory)
{
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));

    /// <summary>
    /// 从磁盘加载指定 Session 的 Pet 状态，构建并返回 <see cref="MicroPet"/>。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// 初始化好的 <see cref="MicroPet"/>；若 Pet 目录或状态文件不存在则返回 <c>null</c>。
    /// </returns>
    public Task<MicroPet?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        return LoadAsync(new ReadOnlyMicroSessionStub(sessionId), ct);
    }

    /// <summary>
    /// 从磁盘加载指定 Session 的 Pet 状态，构建并返回 <see cref="MicroPet"/>。
    /// </summary>
    public async Task<MicroPet?> LoadAsync(IMicroSession microSession, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(microSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(microSession.Id);

        // 加载 Pet 状态（文件不存在返回 null）
        PetState? petState = await _stateStore.LoadAsync(microSession.Id, ct);
        if (petState is null)
            return null;

        // 加载 Pet 配置（文件不存在返回 null，表示 Pet 目录不完整）
        PetConfig? petConfig = await _stateStore.LoadConfigAsync(microSession.Id, ct);
        if (petConfig is null)
            return null;

        // 加载情绪状态（无记录时返回默认平衡情绪）
        EmotionState emotion = await _emotionStore.GetCurrentAsync(microSession.Id, ct);
        PetContextState initialState = microSession.IsApproved
            ? PetContextState.Active
            : PetContextState.Disabled;

        return new MicroPet(
            microSession, petState, petConfig, emotion, initialState,
            decisionEngine, _emotionStore, emotionRuleEngine, emotionBehaviorMapper,
            petRagScope, _stateStore, sessionObserver, rateLimiter,
            reportBuilder, agentStore, providerStore, loggerFactory);
    }

    private sealed class ReadOnlyMicroSessionStub(string sessionId) : IMicroSession
    {
        public string Id { get; } = sessionId;
        public string Title { get; } = sessionId;
        public string ProviderId { get; } = string.Empty;
        public bool IsApproved { get; } = true;
        public Configuration.Options.ChannelType ChannelType { get; } = Configuration.Options.ChannelType.Web;
        public string ChannelId { get; } = "web";
        public SessionEntity Entity { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public string? AgentId => null;
        public string? ApprovalReason => null;
        public MicroClaw.Abstractions.Channel.IChannel? Channel => null;
        public IPet? Pet => null;

        public SessionInfo ToInfo() => new(
            Id,
            Title,
            ProviderId,
            IsApproved,
            ChannelType,
            ChannelId,
            CreatedAt,
            AgentId,
            ApprovalReason);
    }
}
