using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

public interface IModelProvider
{
    bool Supports(ProviderProtocol protocol);
    IChatClient Create(ProviderConfig config);
}
