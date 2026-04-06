using MicroClaw.Abstractions.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Pet.Emotion;
using MicroClaw.Safety;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 级痛觉-情绪联动服务：当高严重度痛觉被记录时，
/// 自动将对应情绪事件施加到与该 Agent 关联的所有 Session 的 Pet 情绪状态，
/// 使其向「谨慎」模式收敛。
/// <para>
/// 联动规则：
/// <list type="bullet">
///   <item><see cref="PainSeverity.High"/> → <see cref="EmotionEventType.PainOccurredHigh"/>（警觉+22, 信心-18）</item>
///   <item><see cref="PainSeverity.Critical"/> → <see cref="EmotionEventType.PainOccurredCritical"/>（警觉+32, 信心-28）</item>
///   <item>Low / Medium → 不触发情绪变化</item>
/// </list>
/// </para>
/// </summary>
public sealed class PainEmotionLinker(
    IEmotionStore emotionStore,
    IEmotionRuleEngine emotionRuleEngine,
    ISessionRepository sessionsReader) : IPainEmotionLinker
{
    private readonly IEmotionStore _emotionStore = emotionStore
        ?? throw new ArgumentNullException(nameof(emotionStore));

    private readonly IEmotionRuleEngine _emotionRuleEngine = emotionRuleEngine
        ?? throw new ArgumentNullException(nameof(emotionRuleEngine));

    private readonly ISessionRepository _sessionsReader = sessionsReader
        ?? throw new ArgumentNullException(nameof(sessionsReader));

    /// <inheritdoc/>
    public async Task LinkAsync(PainMemory memory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // 仅 High / Critical 严重度触发情绪联动
        if (memory.Severity < PainSeverity.High)
            return;

        EmotionEventType eventType = memory.Severity == PainSeverity.Critical
            ? EmotionEventType.PainOccurredCritical
            : EmotionEventType.PainOccurredHigh;

        // 查找与该 Agent 关联的所有 Session，逐一更新 Pet 情绪
        var sessions = _sessionsReader.GetAll()
            .Where(s => s.AgentId == memory.AgentId)
            .ToList();

        foreach (var session in sessions)
        {
            EmotionState current = await _emotionStore.GetCurrentAsync(session.Id, ct);
            EmotionState updated = _emotionRuleEngine.Evaluate(current, eventType);
            await _emotionStore.SaveAsync(session.Id, updated, ct);
        }
    }
}
