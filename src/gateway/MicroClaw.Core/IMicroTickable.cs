namespace MicroClaw.Core;

/// <summary>可被引擎逐帧调度的对象。</summary>
public interface IMicroTickable
{
    /// <summary>执行一次逻辑帧更新。</summary>
    ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default);
}