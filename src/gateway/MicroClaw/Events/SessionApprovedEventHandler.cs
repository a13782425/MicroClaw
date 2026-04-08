using MicroClaw.Abstractions.Events;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Events;

/// <summary>
/// 处理 <see cref="SessionApprovedEvent"/>：审批会话后自动为该会话创建 Pet。
/// <para>
/// 替代原 SessionEndpoints.approve 中硬编码的 <c>petFactory.CreateAsync(req.Id)</c>，
/// 通过领域事件解耦，PetFactory 不再被端点层直接依赖。
/// </para>
/// </summary>
public sealed class SessionApprovedEventHandler(
    ISessionRepository sessionRepo,
    IPetFactory petFactory,
    ILogger<SessionApprovedEventHandler> logger)
    : IDomainEventHandler<SessionApprovedEvent>
{
    public async Task HandleAsync(SessionApprovedEvent domainEvent, CancellationToken ct = default)
    {
        IMicroSession? session = sessionRepo.Get(domainEvent.SessionId);
        if (session is null)
        {
            logger.LogWarning("SessionApprovedEvent 处理跳过：Session {SessionId} 不存在", domainEvent.SessionId);
            return;
        }

        logger.LogInformation("SessionApprovedEvent 处理中：激活 Session {SessionId} 的 Pet", domainEvent.SessionId);
        await petFactory.ActivateAsync(session, ct);
    }
}
