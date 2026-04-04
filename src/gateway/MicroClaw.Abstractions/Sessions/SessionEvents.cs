using MicroClaw.Abstractions.Events;

namespace MicroClaw.Abstractions.Sessions;

/// <summary>会话被审批，准许接入 Agent 处理消息。</summary>
/// <param name="SessionId">被审批的会话 ID。</param>
public sealed record SessionApprovedEvent(string SessionId) : IDomainEvent;

/// <summary>会话被删除（包含软删/硬删），用于触发 Pet/RAG 等资源清理。</summary>
/// <param name="SessionId">被删除的会话 ID。</param>
public sealed record SessionDeletedEvent(string SessionId) : IDomainEvent;

/// <summary>会话关联的 Provider 被切换，用于触发 Provider 相关缓存失效或通知。</summary>
/// <param name="SessionId">发生切换的会话 ID。</param>
/// <param name="OldProviderId">切换前的 Provider ID。</param>
/// <param name="NewProviderId">切换后的 Provider ID。</param>
public sealed record SessionProviderChangedEvent(
    string SessionId,
    string OldProviderId,
    string NewProviderId) : IDomainEvent;
