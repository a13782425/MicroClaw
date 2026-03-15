namespace MicroClaw.Provider.Abstractions.Models;

public sealed record ModelInvokeRequest(string Prompt, string? SystemPrompt = null);

public sealed record ModelInvokeResponse(string Content, string Provider, DateTimeOffset UtcNow);