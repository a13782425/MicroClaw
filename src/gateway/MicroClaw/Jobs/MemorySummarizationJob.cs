using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// B-02: 每日记忆总结后台任务。
/// 每天凌晨 2 点（UTC）执行：
///   1. 为所有活跃 Session 总结前一天的对话消息，写入 memory/{date}.md。
///   2. 每周（逢周一）将 7-13 天前的日记忆要点合并更新到长期记忆 MEMORY.md。
/// </summary>
public sealed class MemorySummarizationJob(
    SessionStore sessionStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    MemoryService memoryService,
    ILogger<MemorySummarizationJob> logger) : BackgroundService
{
    // 每天凌晨 2 点（UTC）执行
    internal static readonly TimeOnly RunTime = new(2, 0, 0);

    // 启动延迟：等待其他 Hosted Service 就绪
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

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

    // 每周合并 Prompt（{existing} 和 {daily} 占位符）
    internal const string WeeklyMergePromptTemplate =
        """
        请将以下最近几天的日记忆内容合并更新到现有长期记忆中（中文，500 字以内）：
        - 保留长期记忆中的核心事实和持续存在的上下文
        - 将新的日记忆要点整合进去，去除重复和过时信息
        - 保持 Markdown 格式，条目清晰

        现有长期记忆（MEMORY.md）：
        {existing}

        最近几天的日记忆：
        {daily}
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("B-02 MemorySummarizationJob 已启动");

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = CalcDelayUntilNextRun();
            logger.LogDebug("B-02 MemorySummarizationJob 下次执行约 {Hours:F1} 小时后", delay.TotalHours);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            if (stoppingToken.IsCancellationRequested) return;

            DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            bool doWeeklyMerge = DateTime.UtcNow.DayOfWeek == DayOfWeek.Monday;

            logger.LogInformation(
                "B-02 MemorySummarizationJob 开始执行，目标日期={Date}，执行长期记忆合并={Weekly}",
                targetDate, doWeeklyMerge);

            await RunSummarizationAsync(targetDate, doWeeklyMerge, stoppingToken);
        }

        logger.LogInformation("B-02 MemorySummarizationJob 已停止");
    }

    /// <summary>计算距离今日 RunTime 的等待时长（保证下次恰好在 RunTime 触发）。内部方法供测试使用。</summary>
    internal static TimeSpan CalcDelayUntilNextRun(DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        DateTime nextRun = now.Date.Add(RunTime.ToTimeSpan());
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        return nextRun - now;
    }

    /// <summary>
    /// 执行一轮记忆总结，供测试直接调用。
    /// 遍历所有 Session：有消息的写入每日记忆；周一时额外合并长期记忆。
    /// </summary>
    internal async Task RunSummarizationAsync(DateOnly date, bool doWeeklyMerge, CancellationToken ct)
    {
        IReadOnlyList<SessionInfo> sessions = sessionStore.All;
        foreach (SessionInfo session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            await SummarizeSessionAsync(session, date, doWeeklyMerge, ct);
        }
    }

    private async Task SummarizeSessionAsync(
        SessionInfo session, DateOnly date, bool doWeeklyMerge, CancellationToken ct)
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

            if (dayMessages.Count > 0)
            {
                ProviderConfig? provider = providerStore.All
                    .FirstOrDefault(p => p.Id == session.ProviderId && p.IsEnabled);

                if (provider is null)
                {
                    logger.LogWarning(
                        "B-02 Session={SessionId} 无可用 Provider（{ProviderId}），跳过每日记忆总结",
                        session.Id, session.ProviderId);
                }
                else
                {
                    IChatClient client = clientFactory.Create(provider);
                    string summary = await BuildDailySummaryAsync(dayMessages, client, ct);
                    memoryService.WriteDailyMemory(session.Id, dateStr, summary);
                    logger.LogInformation(
                        "B-02 Session={SessionId} 每日记忆已写入 {Date}，消息数={Count}",
                        session.Id, dateStr, dayMessages.Count);
                }
            }
            else
            {
                logger.LogDebug("B-02 Session={SessionId} 在 {Date} 无有效消息，跳过", session.Id, dateStr);
            }

            // 2. 每周合并长期记忆（逢周一）
            if (doWeeklyMerge)
                await MergeToLongTermAsync(session, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "B-02 Session={SessionId} 记忆总结异常", session.Id);
        }
    }

    /// <summary>将 7-13 天前的日记忆合并到长期记忆 MEMORY.md（每周执行一次）。</summary>
    private async Task MergeToLongTermAsync(SessionInfo session, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 取 D-7 到 D-13 共 7 天的日记忆
        List<string> dailyContents = Enumerable.Range(7, 7)
            .Select(d => memoryService.GetDailyMemory(session.Id, today.AddDays(-d).ToString("yyyy-MM-dd")))
            .Where(info => info is not null)
            .Select(info => $"## {info!.Date}\n{info.Content}")
            .ToList();

        if (dailyContents.Count == 0)
        {
            logger.LogDebug("B-02 Session={SessionId} 无 7-13 天前的日记忆，跳过长期记忆合并", session.Id);
            return;
        }

        ProviderConfig? provider = providerStore.All
            .FirstOrDefault(p => p.Id == session.ProviderId && p.IsEnabled);

        if (provider is null)
        {
            logger.LogWarning(
                "B-02 Session={SessionId} 无可用 Provider，跳过长期记忆合并", session.Id);
            return;
        }

        IChatClient client = clientFactory.Create(provider);
        string existingMemory = memoryService.GetLongTermMemory(session.Id);
        string merged = await BuildWeeklyMergeAsync(existingMemory, dailyContents, client, ct);
        memoryService.UpdateLongTermMemory(session.Id, merged);
        logger.LogInformation("B-02 Session={SessionId} 长期记忆已合并更新", session.Id);
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

    /// <summary>调用 LLM 将每日记忆合并到长期记忆；返回合并后的文本。</summary>
    internal static async Task<string> BuildWeeklyMergeAsync(
        string existingMemory,
        IReadOnlyList<string> dailyContents,
        IChatClient client,
        CancellationToken ct)
    {
        string existing = string.IsNullOrWhiteSpace(existingMemory) ? "（暂无长期记忆）" : existingMemory;
        string daily = string.Join("\n\n", dailyContents);
        string prompt = WeeklyMergePromptTemplate
            .Replace("{existing}", existing)
            .Replace("{daily}", daily);

        ChatResponse response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);

        return response.Text ?? string.Empty;
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
