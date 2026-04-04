using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;

namespace MicroClaw.Pet;

/// <summary>
/// Per-Session <see cref="PetContext"/> 工厂：从文件系统加载 Pet 状态并构建 <see cref="PetContext"/> 实例。
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
    IEmotionStore emotionStore)
{
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));

    /// <summary>
    /// 从磁盘加载指定 Session 的 Pet 状态，构建并返回 <see cref="PetContext"/>。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// 初始化好的 <see cref="PetContext"/>；若 Pet 目录或状态文件不存在则返回 <c>null</c>。
    /// </returns>
    public async Task<PetContext?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // 加载 Pet 状态（文件不存在返回 null）
        var petState = await _stateStore.LoadAsync(sessionId, ct);
        if (petState is null)
            return null;

        // 加载 Pet 配置（文件不存在返回 null，表示 Pet 目录不完整）
        var petConfig = await _stateStore.LoadConfigAsync(sessionId, ct);
        if (petConfig is null)
            return null;

        // 加载情绪状态（无记录时返回默认平衡情绪）
        var emotion = await _emotionStore.GetCurrentAsync(sessionId, ct);

        return new PetContext(petState, petConfig, emotion);
    }
}
