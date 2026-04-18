using MicroClaw.RAG;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// RAG 上下文提供者 — TODO: Reimplement with MicroRag from session.
/// </summary>
public sealed class RagContextProvider : IUserAwareContextProvider
{
    private readonly RagRetrievalContext _retrievalContext;
    private readonly ILogger<RagContextProvider> _logger;

    public RagContextProvider(
        RagRetrievalContext retrievalContext,
        ILogger<RagContextProvider> logger)
    {
        _retrievalContext = retrievalContext ?? throw new ArgumentNullException(nameof(retrievalContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Order => 15;

    public ValueTask<string?> BuildContextAsync(
        Agent agent,
        string? sessionId,
        CancellationToken ct = default)
        => new(default(string));

    /// <summary>TODO: Reimplement using session.Rag.QueryWithMetadataAsync</summary>
    public ValueTask<string?> BuildContextAsync(
        Agent agent,
        string? sessionId,
        string? userMessage,
        CancellationToken ct = default)
    {
        // Temporarily disabled during MicroRag migration
        return new(default(string));
    }
}
