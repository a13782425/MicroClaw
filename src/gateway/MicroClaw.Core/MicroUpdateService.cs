namespace MicroClaw.Core;

/// <summary>
/// 需要逐帧驱动的服务基类，继承自 <see cref="MicroService"/>。
/// 每次 <see cref="MicroEngine.TickAsync"/> 调用时，引擎会依次调用所有已启动的此类服务的 <see cref="TickAsync"/>。
/// </summary>
public abstract class MicroUpdateService : MicroService
{
    /// <summary>每帧更新逻辑，<paramref name="deltaTime"/> 为距上一帧的时间间隔。</summary>
    public abstract ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default);

    protected override ValueTask OnTickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        => TickAsync(deltaTime, cancellationToken);
}