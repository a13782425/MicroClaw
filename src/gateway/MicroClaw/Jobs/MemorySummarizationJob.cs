using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Providers;
using MicroClaw.RAG;
using MicroClaw.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// B-02: 每日记忆总结后台任务。
/// 每天凌晨 2 点（UTC）执行：
///   1. 为所有活跃 Session 总结前一天的对话消息，写入 memory/{date}.md。
///   2. 将同天日记忆按主题分类归纳，更新 RAG 分类 chunk 和 MEMORY.md 目录。
///   3. 清理 3 天前的日记忆文件。
/// </summary>
public sealed class MemorySummarizationJob(
    SessionStore sessionStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    MemoryService memoryService,
    IRagService ragService,
    ILogger<MemorySummarizationJob> logger) : IScheduledJob
{
    // 每天凌晨 2 点（UTC）执行
    internal static readonly TimeOnly RunTime = new(2, 0, 0);

    public string JobName => "memory-summarization";
    public JobSchedule Schedule => new JobSchedule.DailyAt(RunTime, TimeSpan.FromSeconds(60));

    // 每日总结 Prompt（{messages} 占位符由 FormatMessages 替换）
    internal const string DailySummaryPromptTemplate =
        """
        请将以下对话历史总结为简洁的日记忆笔记（中文，300 字以内）：
        - 记录用户的主要意图、任务和重要决策
        - 记录 AI 提供的关键建议或操作结果
        - 忽略闲聊和无实质内容的消息

        输出格式：直接输出 Markdown 正文，无需标题，每个要点一行，以 `-` 开头。

        对话历史：
        {messages}
        """;

    // 分类归纳 Prompt（{existing_categories} 和 {daily_summary} 占位符）
    internal const string CategoryClassificationPromptTemplate =
        """
        你是记忆管理助手。请将今日记忆内容按主题分类整合到已有的分类记忆中。

        要求：
        - 保留已有分类，必要时新增分类（最多保留 10 个分类）
        - 每个分类内容精简（100 字以内），去除重复和过时信息
        - 输出格式必须是合法的 JSON 对象：{"分类名": "分类内容正文", ...}
        - 常见分类示例：项目进度、技术偏好、决策历史、用户习惯、待办事项
        - 只输出 JSON，不要包含其他内容或 Markdown 代码块

        已有分类记忆（JSON）：
        {existing_categories}

        今日记忆摘要：
        {daily_summary}
        """;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        logger.LogInformation("B-02 MemorySummarizationJob 开始执行，目标日期={Date}", targetDate);
        await RunSummarizationAsync(targetDate, ct);
    }

    /// <summary>计算距离下次 RunTime 的等待时长。保留供测试使用。</summary>
    internal static TimeSpan CalcDelayUntilNextRun(DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        DateTime nextRun = now.Date.Add(RunTime.ToTimeSpan());
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        return nextRun - now;
    }

    /// <summary>
    /// 执行一轮记忆总结，供测试直接调用。
    /// 遍历所有 Session：有消息的写入每日记忆，并触发分类归纳。
    /// </summary>
    internal async Task RunSummarizationAsync(DateOnly date, CancellationToken ct)
    {
        IReadOnlyList<SessionInfo> sessions = sessionStore.All;
        foreach (SessionInfo session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            await SummarizeSessionAsync(session, date, ct);
        }
    }

    private async Task SummarizeSessionAsync(
        SessionInfo session, DateOnly date, CancellationToken ct)
    {
        try
        {
            string dateStr = date.ToString("yyyy-MM-dd");

            // 1. 每日记忆：取目标日期的 user/assistant 消息
            IReadOnlyList<SessionMessage> allMessages = sessionStore.GetMessages(session.Id);
            List<SessionMessage> dayMessages = allMessages
                .Where(m => DateOnly.FromDateTime(m.Timestamp.UtcDateTime) == date)
                .Where(m => m.Role is "user" or "assistant")
                .ToList();

            ProviderConfig? provider = providerStore.All
                .FirstOrDefault(p => p.Id == session.ProviderId && p.IsEnabled);
            IChatClient? client = provider is not null ? clientFactory.Create(provider) : null;

            if (dayMessages.Count > 0)
            {
                // 与 ContextOverflowSummarizer 协调：溢出已写入时，只处理更新后的消息
                DailyMemoryInfo? existingDailyMemory = memoryService.GetDailyMemory(session.Id, dateStr);
                if (existingDailyMemory is not null)
                {
                    dayMessages = dayMessages
                        .Where(m => m.Timestamp > existingDailyMemory.UpdatedAt)
                        .ToList();
                }

                if (dayMessages.Count > 0)
                {
                    if (client is null)
                    {
                        logger.LogWarning(
                            "B-02 Session={SessionId} 无可用 Provider（{ProviderId}），跳过每日记忆总结",
                            session.Id, session.ProviderId);
                    }
                    else
                    {
                        string summary = await BuildDailySummaryAsync(dayMessages, client, ct);
                        memoryService.WriteDailyMemory(session.Id, dateStr, summary);
                        logger.LogInformation(
                            "B-02 Session={SessionId} 每日记忆已写入 {Date}，消息数={Count}",
                            session.Id, dateStr, dayMessages.Count);
                    }
                }
                else
                {
                    logger.LogDebug(
                        "B-02 Session={SessionId} 在 {Date} 已有溢出总结且无新消息，跳过每日总结",
                        session.Id, dateStr);
                }
            }
            else
            {
                logger.LogDebug("B-02 Session={SessionId} 在 {Date} 无有效消息，跳过", session.Id, dateStr);
            }

            // 2. 分类归纳：基于今日日记忆更新 RAG 分类 chunk 和 MEMORY.md 目录
            if (client is not null)
            {
                DailyMemoryInfo? dailyForDate = memoryService.GetDailyMemory(session.Id, dateStr);
                if (dailyForDate is not null && !string.IsNullOrWhiteSpace(dailyForDate.Content))
                    await ClassifyToCategoriesAsync(session, dateStr, client, ct);
            }

            // 3. 清理 3 天前的日记忆文件
            CleanupOldDailyMemories(session, date);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "B-02 Session={SessionId} 记忆总结异常", session.Id);
        }
    }

    /// <summary>
    /// 将日记忆内容按主题分类，更新 Session RAG 分类 chunk 和 MEMORY.md 目录。
    /// </summary>
    private async Task ClassifyToCategoriesAsync(
        SessionInfo session, string dateStr, IChatClient client, CancellationToken ct)
    {
        string existingJson = memoryService.GetCategoriesJson(session.Id);
        DailyMemoryInfo? daily = memoryService.GetDailyMemory(session.Id, dateStr);
        if (daily is null) return;

        string updatedJson = await BuildCategoryClassificationAsync(existingJson, daily.Content, client, ct);
        if (string.IsNullOrWhiteSpace(updatedJson))
        {
            logger.LogWarning("B-02 Session={SessionId} 分类 LLM 返回空结果，跳过", session.Id);
            return;
        }

        Dictionary<string, string>? categories;
        try
        {
            categories = JsonSerializer.Deserialize<Dictionary<string, string>>(updatedJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "B-02 Session={SessionId} 分类 JSON 解析失败：{Json}",
                session.Id, updatedJson[..Math.Min(200, updatedJson.Length)]);
            return;
        }

        if (categories is null || categories.Count == 0) return;

        foreach (var (categoryName, content) in categories)
        {
            try
            {
                await ragService.DeleteBySourceIdAsync(categoryName, RagScope.Session, session.Id, ct);
                await ragService.IngestAsync(content, categoryName, RagScope.Session, session.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "B-02 Session={SessionId} 分类 '{Category}' 写入 RAG 异常",
                    session.Id, categoryName);
            }
        }

        string newJson = JsonSerializer.Serialize(categories, new JsonSerializerOptions { WriteIndented = true });
        memoryService.WriteCategoriesJson(session.Id, newJson);
        memoryService.UpdateLongTermMemory(session.Id, BuildCategoryIndex(categories));
        logger.LogInformation("B-02 Session={SessionId} 分类记忆已更新，共 {Count} 个分类",
            session.Id, categories.Count);
    }

    /// <summary>
    /// 删除 3 天前的日记忆文件（不再负责 RAG cleanup，分类 chunk 由分类作业单独管理）。
    /// </summary>
    private void CleanupOldDailyMemories(SessionInfo session, DateOnly today)
    {
        IReadOnlyList<string> allDates = memoryService.ListDailyMemories(session.Id);
        int deletedCount = 0;

        foreach (string date in allDates)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateOnly memDate))
                continue;

            int daysAgo = today.DayNumber - memDate.DayNumber;
            if (daysAgo <= 3) continue;

            if (memoryService.DeleteDailyMemory(session.Id, date))
                deletedCount++;
        }

        if (deletedCount > 0)
        {
            logger.LogInformation(
                "B-02 Session={SessionId} 清理了 {Count} 个过期日记忆文件",
                session.Id, deletedCount);
        }
    }

    // ── 静态 helpers（internal 供测试调用）────────────────────────────────────

    /// <summary>调用 LLM 总结每日消息；返回摘要文本。</summary>
    internal static async Task<string> BuildDailySummaryAsync(
        IReadOnlyList<SessionMessage> messages,
        IChatClient client,
        CancellationToken ct)
    {
        string formatted = FormatMessages(messages);
        string prompt = DailySummaryPromptTemplate.Replace("{messages}", formatted);

        ChatResponse response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);

        return response.Text ?? string.Empty;
    }

    /// <summary>调用 LLM 将今日摘要归纳到已有分类记忆中；返回更新后的分类 JSON 字符串。</summary>
    internal static async Task<string> BuildCategoryClassificationAsync(
        string existingCategoriesJson,
        string dailySummary,
        IChatClient client,
        CancellationToken ct)
    {
        string existing = string.IsNullOrWhiteSpace(existingCategoriesJson) ? "{}" : existingCategoriesJson;
        string prompt = CategoryClassificationPromptTemplate
            .Replace("{existing_categories}", existing)
            .Replace("{daily_summary}", dailySummary);

        ChatResponse response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);

        string text = (response.Text ?? string.Empty).Trim();

        // 若 LLM 将 JSON 包裹在 Markdown 代码块中，提取纯 JSON
        if (text.Contains("```"))
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                text = text[start..(end + 1)];
        }

        return text;
    }

    /// <summary>从分类字典生成 MEMORY.md 目录内容。</summary>
    private static string BuildCategoryIndex(Dictionary<string, string> categories)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 记忆目录");
        sb.AppendLine();
        sb.AppendLine("以下是会话的长期记忆分类索引，详细内容可通过语义检索获取：");
        sb.AppendLine();
        foreach (var (name, content) in categories)
        {
            string summary = content.Trim();
            int dotIdx = summary.IndexOfAny(['.', '。', '\n'], 0);
            if (dotIdx > 0 && dotIdx < 80)
                summary = summary[..dotIdx];
            else if (summary.Length > 80)
                summary = summary[..80] + "…";
            sb.AppendLine($"- **{name}**：{summary}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>将消息列表格式化为 LLM 可读文本（超过 500 字符的消息截断）。</summary>
    internal static string FormatMessages(IReadOnlyList<SessionMessage> messages)
    {
        return string.Join("\n", messages.Select(m =>
        {
            string role = m.Role == "user" ? "用户" : "AI";
            string content = m.Content.Length > 500 ? m.Content[..500] + "..." : m.Content;
            return $"[{role}]: {content}";
        }));
    }
}
