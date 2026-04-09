using MicroClaw.Abstractions.Sessions;
using MicroClaw.Jobs;
using MicroClaw.Pet.Emotion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// P-B-7: Pet 情绪自然衰减 Job。
/// 每小时执行，将所有活跃 Session 的 Pet 四维情绪（警觉度/心情/好奇心/信心）
/// 向默认值（50）靠近，防止单次痛觉/失败事件长期改变 Pet 的行为模式。
/// <para>
/// 衰减规则：每维度偏差超过 <see cref="DecayStep"/> 时减去 <see cref="DecayStep"/>，
/// 偏差小于等于 <see cref="DecayStep"/> 时直接归中到 <see cref="DefaultValue"/>。
/// </para>
/// </summary>
public sealed class PetEmotionDecayJob : IScheduledJob
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IEmotionStore _emotionStore;
    private readonly ILogger<PetEmotionDecayJob> _logger;

    public PetEmotionDecayJob(IServiceProvider sp)
    {
        _sessionRepo = sp.GetRequiredService<ISessionRepository>();
        _emotionStore = sp.GetRequiredService<IEmotionStore>();
        _logger = sp.GetRequiredService<ILogger<PetEmotionDecayJob>>();
    }
    /// <summary>每次衰减的步长（朝 50 方向靠近的绝对值）。</summary>
    internal const int DecayStep = 3;

    /// <summary>情绪各维度的中性默认值。</summary>
    internal const int DefaultValue = EmotionState.DefaultValue;

    public string JobName => "pet-emotion-decay";

    public JobSchedule Schedule => new JobSchedule.FixedInterval(
        Interval: TimeSpan.FromHours(1),
        StartupDelay: TimeSpan.FromMinutes(5));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var sessions = _sessionRepo.GetAll();
        int decayed = 0;

        foreach (var session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            if (!session.IsApproved) continue;

            EmotionState current;

            // 若 Pet 已加载到内存，直接使用内存情绪快照；否则从磁盘加载
            var petCtx = session.Pet as PetContext;
            if (petCtx is not null)
            {
                current = petCtx.Emotion;
            }
            else
            {
                current = await _emotionStore.GetCurrentAsync(session.Id, ct);
            }

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

            // 若 PetContext 已在内存中，通过 UpdateEmotion 同步内存快照
            if (petCtx is not null)
                petCtx.UpdateEmotion(next);

            await _emotionStore.SaveAsync(session.Id, next, ct);
            decayed++;
        }

        if (decayed > 0)
            _logger.LogInformation("Pet 情绪衰减完成：共衰减 {Count} 个 Session 的情绪状态", decayed);
    }

    private static int Decay(int value)
    {
        int delta = value - DefaultValue;
        if (delta == 0) return DefaultValue;
        if (Math.Abs(delta) <= DecayStep) return DefaultValue;
        return delta > 0 ? value - DecayStep : value + DecayStep;
    }
}
