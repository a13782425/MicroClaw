using MicroClaw.Agent;
using MicroClaw.Emotion;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// B-03: 情绪自然衰减 Job。
/// 每小时执行，将所有已启用 Agent 的四维情绪（警觉度/心情/好奇心/信心）向默认值（50）靠近，
/// 防止单次痛觉/失败事件长期改变行为模式。
/// 衰减步长：偏差超过 DecayStep 时减去 DecayStep，偏差小于等于 DecayStep 时直接归中。
/// </summary>
public sealed class EmotionDecayJob(
    AgentStore agentStore,
    IEmotionStore emotionStore,
    ILogger<EmotionDecayJob> logger) : IScheduledJob
{
    /// <summary>每次衰减的步长（朝 50 方向靠近的绝对值）。</summary>
    internal const int DecayStep = 3;

    /// <summary>情绪各维度的中性默认值。</summary>
    internal const int DefaultValue = EmotionState.DefaultValue;

    public string JobName => "emotion-decay";

    public JobSchedule Schedule => new JobSchedule.FixedInterval(
        Interval: TimeSpan.FromHours(1),
        StartupDelay: TimeSpan.FromMinutes(5));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        IReadOnlyList<AgentConfig> agents = agentStore.All;
        int decayed = 0;

        foreach (AgentConfig agent in agents)
        {
            if (ct.IsCancellationRequested) break;
            if (!agent.IsEnabled) continue;

            EmotionState current = await emotionStore.GetCurrentAsync(agent.Id, ct);

            // 如果已全部处于默认值，跳过写入
            if (current.Alertness == DefaultValue &&
                current.Mood == DefaultValue &&
                current.Curiosity == DefaultValue &&
                current.Confidence == DefaultValue)
                continue;

            EmotionState next = new(
                alertness: Decay(current.Alertness),
                mood: Decay(current.Mood),
                curiosity: Decay(current.Curiosity),
                confidence: Decay(current.Confidence));

            await emotionStore.SaveAsync(agent.Id, next, ct);
            decayed++;

            logger.LogDebug(
                "EmotionDecayJob: Agent [{AgentId}] 情绪衰减 Alertness={A}→{NA} Mood={M}→{NM} Curiosity={C}→{NC} Confidence={Co}→{NCo}",
                agent.Id,
                current.Alertness, next.Alertness,
                current.Mood, next.Mood,
                current.Curiosity, next.Curiosity,
                current.Confidence, next.Confidence);
        }

        if (decayed > 0)
            logger.LogInformation("EmotionDecayJob: 已对 {Count} 个 Agent 执行情绪衰减", decayed);
        else
            logger.LogDebug("EmotionDecayJob: 所有 Agent 情绪已处于默认值，无需衰减");
    }

    private static int Decay(int value)
    {
        int diff = value - DefaultValue;
        if (diff == 0) return DefaultValue;
        int step = Math.Min(Math.Abs(diff), DecayStep);
        return value - Math.Sign(diff) * step;
    }
}
