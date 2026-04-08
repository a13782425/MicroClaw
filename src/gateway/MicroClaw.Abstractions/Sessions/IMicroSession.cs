using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Session runtime contract exposed across module boundaries.
/// </summary>
public interface IMicroSession
{
    string Id { get; }
    string Title { get; }
    string ProviderId { get; }
    bool IsApproved { get; }
    ChannelType ChannelType { get; }
    string ChannelId { get; }
    DateTimeOffset CreatedAt { get; }
    string? AgentId { get; }
    string? ApprovalReason { get; }
    IChannel? Channel { get; }
    IPet? Pet { get; }
    IReadOnlyList<Events.IDomainEvent> PopDomainEvents();
    SessionInfo ToInfo();
}
