using MicroClaw.Abstractions.Sessions;

namespace MicroClaw.Abstractions.Streaming;

/// <summary>
/// StreamItem 持久化处理器接口。每种 StreamItem 子类型对应一个 Handler，
/// 负责将流事件转为 <see cref="SessionMessage"/> 用于数据库持久化。
/// </summary>
public interface IStreamItemPersistenceHandler
{
    /// <summary>判断是否能处理该 StreamItem 类型。</summary>
    bool CanHandle(StreamItem item);

    /// <summary>
    /// 将 StreamItem 转为要持久化的 SessionMessage。返回 null 表示该类型不需要立即持久化（如 TokenItem 需要聚合）。
    /// </summary>
    SessionMessage? ToPersistenceMessage(StreamItem item);
}
