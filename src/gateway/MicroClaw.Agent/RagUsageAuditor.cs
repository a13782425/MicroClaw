using System.Text.Json;
using MicroClaw.Providers;
using MicroClaw.RAG;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// RagUsageAuditor 实现：使用 LLM 比对检索到的 chunk 与 AI 实际回复，
/// 确定真正被使用的 chunk 并精确更新 HitCount。
/// </summary>
public sealed class RagUsageAuditor : IRagUsageAuditor
{
    private readonly IRagService _ragService;
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
        IRagService ragService,
        ProviderService providerService,
        ILogger<RagUsageAuditor> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
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
                _logger.LogWarning("RAG 审计：无可用 Provider，跳过审计，回退到全量 HitCount 更新");
                await FallbackIncrementAllAsync(retrievedChunks, ct);
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

            if (usedIds.Count == 0)
            {
                _logger.LogDebug("RAG 审计：AI 判定无 chunk 被实际使用");
                return;
            }

            // Group by scope+sessionId to batch updates
            var groups = retrievedChunks
                .Where(c => usedIds.Contains(c.Id))
                .GroupBy(c => (c.Scope, c.SessionId));

            foreach (var group in groups)
            {
                var ids = group.Select(c => c.Id).ToList();
                await _ragService.IncrementHitCountAsync(ids, group.Key.Scope, group.Key.SessionId, ct);
            }

            _logger.LogInformation(
                "RAG 审计完成：{Total} 个检索 chunk 中 {Used} 个被确认使用",
                retrievedChunks.Count, usedIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RAG 审计异常，回退到全量 HitCount 更新");
            await FallbackIncrementAllAsync(retrievedChunks, ct);
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

    private async Task FallbackIncrementAllAsync(
        IReadOnlyList<RagChunkRef> chunks, CancellationToken ct)
    {
        try
        {
            var groups = chunks.GroupBy(c => (c.Scope, c.SessionId));
            foreach (var group in groups)
            {
                var ids = group.Select(c => c.Id).ToList();
                await _ragService.IncrementHitCountAsync(ids, group.Key.Scope, group.Key.SessionId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RAG 审计回退更新也失败，HitCount 未更新");
        }
    }
}
