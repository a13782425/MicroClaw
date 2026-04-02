using MicroClaw.Emotion;
using MicroClaw.Infrastructure;
using MicroClaw.Safety;

namespace MicroClaw.Agent;

/// <summary>
/// 痛觉-情绪联动服务：当高严重度痛觉（High / Critical）被记录时，
/// 自动将对应情绪事件施加到指定 Agent 的情绪状态，使其向「谨慎」模式收敛。
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
    IEmotionRuleEngine emotionRuleEngine) : IPainEmotionLinker
{
    private readonly IEmotionStore _emotionStore = emotionStore
        ?? throw new ArgumentNullException(nameof(emotionStore));

    private readonly IEmotionRuleEngine _emotionRuleEngine = emotionRuleEngine
        ?? throw new ArgumentNullException(nameof(emotionRuleEngine));

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

        EmotionState current = await _emotionStore.GetCurrentAsync(memory.AgentId, ct);
        EmotionState updated = _emotionRuleEngine.Evaluate(current, eventType);
        await _emotionStore.SaveAsync(memory.AgentId, updated, ct);
    }
}
