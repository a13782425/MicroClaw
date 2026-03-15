using MicroClaw.Provider.Abstractions.Models;

namespace MicroClaw.Provider.Abstractions;

public interface IModelProvider
{
    string Name { get; }

    Task<ModelInvokeResponse> CompleteAsync(ModelInvokeRequest request, CancellationToken cancellationToken = default);
}