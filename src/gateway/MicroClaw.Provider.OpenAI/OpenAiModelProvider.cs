using MicroClaw.Provider.Abstractions;
using MicroClaw.Provider.Abstractions.Models;

namespace MicroClaw.Provider.OpenAI;

public sealed class OpenAiModelProvider : IModelProvider
{
    public string Name => "OpenAI";

    public Task<ModelInvokeResponse> CompleteAsync(ModelInvokeRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModelInvokeResponse(
            Content: $"[OpenAI placeholder] {request.Prompt}",
            Provider: Name,
            UtcNow: DateTimeOffset.UtcNow));
    }
}