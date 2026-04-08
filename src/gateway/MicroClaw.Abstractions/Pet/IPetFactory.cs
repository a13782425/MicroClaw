using MicroClaw.Abstractions.Sessions;

namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// Contract for creating or loading runtime Pet instances for a Session.
/// </summary>
public interface IPetFactory
{
    Task<IPet?> CreateOrLoadAsync(IMicroSession microSession, CancellationToken ct = default);

    Task<IPet?> ActivateAsync(IMicroSession microSession, CancellationToken ct = default);
}
