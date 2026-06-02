namespace MicroClaw.Core;
/// <summary>可被引擎逐帧调度的对象。</summary>
public interface IMicroTickable
{
    /// <summary>
    /// 每秒执行的逻辑帧数。引擎将以此频率调用 <see cref="TickAsync"/> 方法。
    /// <para>最小1帧，最大120帧</para>
    /// </summary>
    uint Frame => 10;
    /// <summary>
    /// 执行一次逻辑帧更新。
    /// </summary>
    ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default);
}