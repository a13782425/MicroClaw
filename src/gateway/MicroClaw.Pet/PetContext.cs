using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Pet.Emotion;

namespace MicroClaw.Pet;

/// <summary>
/// Per-Session Pet 上下文的具体实现（<see cref="IPetContext"/>）。
/// <para>
/// 作为纯状态持有者（Pure State Holder），持有当前会话 Pet 的三类状态快照：
/// <list type="bullet">
///   <item><see cref="PetState"/> — 行为状态（BehaviorState）与运行时快照</item>
///   <item><see cref="Config"/> — Per-Session Pet 配置（Enabled / 限流 / 允许 Agent 列表等）</item>
///   <item><see cref="Emotion"/> — 四维情绪快照</item>
/// </list>
/// </para>
/// <para>
/// 生命周期：由 <see cref="PetContextFactory"/> 在审批时创建（或在首次使用时懒加载），
/// 通过 <c>Session.AttachPet(petCtx)</c> 挂载到会话上。
/// 会话删除时由 <see cref="IDisposable.Dispose"/> 标记失效。
/// </para>
/// <para>
/// 线程安全注意：<see cref="MarkDirty"/> 使用 volatile 保证可见性，
/// 但 <see cref="UpdateEmotion"/> / <see cref="UpdateBehaviorState"/> 不保证原子性，
/// 调用方应确保单一会话的并发写入通过上层协调（例如消息队列）串行化。
/// </para>
/// </summary>
public sealed class PetContext : IPetContext, IDisposable
{
    private PetState _petState;
    private volatile bool _isDirty;
    private bool _disposed;

    internal PetContext(IMicroSession microSession, PetState state, PetConfig config, EmotionState emotion, PetContextState initialState)
    {
        MicroSession = microSession ?? throw new ArgumentNullException(nameof(microSession));
        _petState = state ?? throw new ArgumentNullException(nameof(state));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Emotion = emotion;
        State = initialState;
    }

    // ── IPetContext ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public PetContextState State { get; private set; }

    /// <inheritdoc/>
    public IMicroSession MicroSession { get; }

    /// <inheritdoc/>
    public bool IsEnabled => !_disposed && State == PetContextState.Active && Config.Enabled;

    /// <inheritdoc/>
    public void MarkDirty() => _isDirty = true;

    // ── 状态数据（只读外部，内部可变）────────────────────────────────────────

    /// <summary>当前 Pet 行为状态快照（不可变 record）。</summary>
    public PetState PetState => _petState;

    /// <summary>Per-Session Pet 配置（可变 class，外部可直接修改属性后持久化）。</summary>
    public PetConfig Config { get; }

    /// <summary>当前四维情绪状态快照（不可变 record）。</summary>
    public EmotionState Emotion { get; private set; }

    /// <summary>是否有待持久化的未保存变更。</summary>
    public bool IsDirty => _isDirty;

    // ── O-3-2: 状态操作方法 ───────────────────────────────────────────────────

    /// <summary>
    /// 将情绪增减量 <paramref name="delta"/> 应用到当前情绪状态，更新内存快照并标记 Dirty。
    /// </summary>
    public void UpdateEmotion(EmotionDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Emotion = Emotion.Apply(delta);
        MarkDirty();
    }

    /// <summary>
    /// 将当前情绪直接替换为 <paramref name="newEmotion"/>，更新内存快照并标记 Dirty。
    /// </summary>
    public void UpdateEmotion(EmotionState newEmotion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Emotion = newEmotion ?? throw new ArgumentNullException(nameof(newEmotion));
        MarkDirty();
    }

    /// <summary>
    /// 更新 Pet 行为状态，记录变更时间并标记 Dirty。
    /// </summary>
    public void UpdateBehaviorState(PetBehaviorState newBehaviorState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _petState = _petState with
        {
            BehaviorState = newBehaviorState,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        MarkDirty();
    }

    /// <summary>
    /// 清除脏标记（由持久化层在写盘后调用）。
    /// </summary>
    public void ClearDirty() => _isDirty = false;

    /// <summary>
    /// Activates the current PetContext without replacing the runtime object.
    /// </summary>
    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        State = PetContextState.Active;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// 将 PetContext 标记为已释放（Disabled）。
    /// 会话删除时由 <see cref="SessionDeletedEventHandler"/> 调用，
    /// 防止此后 PetRunner 继续使用已失效的状态。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        State = PetContextState.Disabled;
    }
}
