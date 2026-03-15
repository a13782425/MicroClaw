using MicroClaw.Provider.Abstractions;
using MicroClaw.Provider.Abstractions.Models;

namespace MicroClaw.Provider.Claude;

public sealed class ClaudeModelProvider : IModelProvider
{
    public string Name => "Claude";

    public Task<ModelInvokeResponse> CompleteAsync(ModelInvokeRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModelInvokeResponse(
            Content: $"[Claude placeholder] {request.Prompt}",
            Provider: Name,
            UtcNow: DateTimeOffset.UtcNow));
    }
}