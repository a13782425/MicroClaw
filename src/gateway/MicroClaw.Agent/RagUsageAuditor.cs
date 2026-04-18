using System.Text.Json;
using MicroClaw.Providers;
using MicroClaw.RAG;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// RagUsageAuditor: uses LLM to compare retrieved chunks vs AI response,
/// identifies actually used chunks. HitCount updates temporarily disabled during MicroRag migration.
/// </summary>
public sealed class RagUsageAuditor : IRagUsageAuditor
{
    private readonly ProviderService _providerService;
    private readonly ILogger<RagUsageAuditor> _logger;

    internal const string AuditPromptTemplate =
        """
        请分析以下 AI 回复中实际使用了哪些检索到的知识片段。

        "使用"的判定标准：AI 回复的内容明显引用、参考或基于某个知识片段的信息，
        包括直接引用、改写、总结或据此做出判断的情况。

        检索到的知识片段（每个片段有唯一 ID）：
        {chunks}

        AI 的实际回复：
        {response}

        请仅输出一个 JSON 数组，包含实际被使用的片段 ID（字符串），例如：
        ["id1", "id2"]

        如果没有任何片段被使用，输出空数组：[]
        不要输出 JSON 以外的任何内容。
        """;

    public RagUsageAuditor(
        ProviderService providerService,
        ILogger<RagUsageAuditor> logger)
    {
        _providerService = providerService ?? throw new ArgumentNullException(nameof(providerService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AuditAsync(
        IReadOnlyList<RagChunkRef> retrievedChunks,
        string assistantResponse,
        CancellationToken ct = default)
    {
        if (retrievedChunks.Count == 0 || string.IsNullOrWhiteSpace(assistantResponse))
            return;

        try
        {
            IChatClient? client = CreateAuditClient();
            if (client is null)
            {
                _logger.LogWarning("RAG 审计：无可用 Provider，跳过审计");
                return;
            }

            string chunksText = FormatChunksForAudit(retrievedChunks);
            string prompt = AuditPromptTemplate
                .Replace("{chunks}", chunksText)
                .Replace("{response}", assistantResponse);

            ChatResponse response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                cancellationToken: ct);

            string resultText = response.Text ?? string.Empty;
            List<string> usedIds = ParseUsedIds(resultText);

            // TODO: Use MicroRag.IncrementHitCountAsync to update HitCount for used chunks
            _logger.LogInformation(
                "RAG 审计完成：{Total} 个检索 chunk 中 {Used} 个被确认使用 (HitCount 更新暂时禁用)",
                retrievedChunks.Count, usedIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RAG 审计异常，HitCount 未更新");
        }
    }

    private IChatClient? CreateAuditClient()
    {
        ProviderConfig? provider = _providerService.GetDefault();
        if (provider is null || !provider.IsEnabled)
            provider = _providerService.All.FirstOrDefault(p => p.IsEnabled);

        return provider is not null ? _providerService.CreateClient(provider) : null;
    }

    private static string FormatChunksForAudit(IReadOnlyList<RagChunkRef> chunks)
    {
        var lines = new List<string>(chunks.Count);
        foreach (var chunk in chunks)
        {
            string content = chunk.Content.Length > 500
                ? chunk.Content[..500] + "..."
                : chunk.Content;
            lines.Add($"[ID: {chunk.Id}]\n{content}");
        }
        return string.Join("\n\n", lines);
    }

    internal static List<string> ParseUsedIds(string resultText)
    {
        string cleaned = resultText.Trim();
        if (cleaned.StartsWith("```"))
        {
            int firstNewline = cleaned.IndexOf('\n');
            int lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
                cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(cleaned);
            return ids ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
