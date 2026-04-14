using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Options;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using Microsoft.Extensions.DependencyInjection;
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
///   <item>服务重启后，PetService 会话处理时发现 Session.Pet 为 null，懒加载。</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetContextFactory
{
    private readonly IServiceProvider _sp;
    private readonly PetStateStore _stateStore;
    private readonly IEmotionStore _emotionStore;

    public PetContextFactory(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _emotionStore = sp.GetRequiredService<IEmotionStore>();
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
        PetContextState initialState = microSession.IsApproved ? PetContextState.Active : PetContextState.Disabled;
        
        return new MicroPet(_sp, microSession, petState, petConfig, emotion, initialState);
    }
}
