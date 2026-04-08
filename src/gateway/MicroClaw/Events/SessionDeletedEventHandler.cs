using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent.Memory;
using MicroClaw.Pet.Rag;
using MicroClaw.Sessions;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Events;

/// <summary>
/// 处理 <see cref="SessionDeletedEvent"/>：删除会话前释放 Pet 资源并清理相关文件。
/// <para>
/// 执行顺序（均在 <c>repo.Delete()</c> 之前）：
/// <list type="ordered">
///   <item>调用 <c>session.PetContext?.Dispose()</c> 将 Per-Session PetContext 标记为已失效（O-3-10）</item>
///   <item>调用 <c>petRagScope.CloseDatabase()</c> 释放 SQLite 连接池文件锁</item>
///   <item>调用 <c>sessionDna.DeleteSessionDnaFiles()</c> 清理 DNA 文件</item>
/// </list>
/// </para>
/// </summary>
public sealed class SessionDeletedEventHandler(
    ISessionRepository sessionRepo,
    PetRagScope petRagScope,
    SessionDnaService sessionDna,
    ILogger<SessionDeletedEventHandler> logger)
    : IDomainEventHandler<SessionDeletedEvent>
{
    public Task HandleAsync(SessionDeletedEvent domainEvent, CancellationToken ct = default)
    {
        string sessionId = domainEvent.SessionId;

        // 0. 释放 Per-Session PetContext（标记 Disabled，防止后续 PetRunner 使用已失效状态）
        IMicroSession? session = sessionRepo.Get(sessionId);
        if (session?.Pet is IDisposable disposable)
        {
            disposable.Dispose();
            if (session is MicroSession mutableSession)
                mutableSession.DetachPet();
            logger.LogDebug("Session {SessionId} 的 Pet 已释放", sessionId);
        }

        // 1. 关闭 Pet RAG SQLite 连接，释放文件锁
        petRagScope.CloseDatabase(sessionId);
        logger.LogDebug("Session {SessionId} 的 Pet RAG 数据库连接已关闭", sessionId);

        // 2. 删除会话固定 DNA 文件（USER.md / AGENTS.md）
        sessionDna.DeleteSessionDnaFiles(sessionId);
        logger.LogDebug("Session {SessionId} 的 DNA 文件已清理", sessionId);

        return Task.CompletedTask;
    }
}
