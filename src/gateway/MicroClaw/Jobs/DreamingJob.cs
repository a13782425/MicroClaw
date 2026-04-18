using MicroClaw.Abstractions;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// D-2: 做梦模式（离线认知整理）。
/// 每天凌晨 3 点（UTC）执行，错开 B-02 MemorySummarizationJob（凌晨 2 点）：
///   1. 遍历所有已启用的 Agent。
///   2. 收集该 Agent 关联的所有 Session 最近 7 天的日记忆片段。
///   3. 结合 Agent 当前的 MEMORY.md，通过 LLM 进行跨 Session 归因/摘要/洞察整理。
///   4. 将整理结果更新写回 Agent 的 MEMORY.md（Agent DNA），实现认知沉淀。
/// </summary>
public sealed class DreamingJob : IScheduledJob
{
    private readonly AgentStore _agentStore;
    private readonly ISessionService _repo;
    private readonly ProviderService _providerService;
    private readonly AgentDnaService _agentDnaService;
    private readonly MemoryService _memoryService;
    private readonly ILogger<DreamingJob> _logger;

    public DreamingJob(IServiceProvider sp)
    {
        _agentStore = sp.GetRequiredService<AgentStore>();
        _repo = sp.GetRequiredService<ISessionService>();
        _providerService = sp.GetRequiredService<ProviderService>();
        _agentDnaService = sp.GetRequiredService<AgentDnaService>();
        _memoryService = sp.GetRequiredService<MemoryService>();
        _logger = sp.GetRequiredService<ILogger<DreamingJob>>();
    }
    public string JobName => "dreaming";
    public JobSchedule Schedule => new JobSchedule.DailyAt(RunTime, TimeSpan.FromSeconds(90));

    // 每天凌晨 3 点（UTC）执行
    internal static readonly TimeOnly RunTime = new(3, 0, 0);

    // 收集最近几天日记忆的回溯窗口
    internal const int DailyMemoryLookbackDays = 7;

    // 认知归因 Prompt 模板（{existing} 和 {memories} 由运行时替换）
    internal const string CognitiveDreamPromptTemplate =
        """
        请对以下来自不同会话的记忆片段进行离线认知整理（中文，500 字以内）：
        - 归因：识别成功和失败的对话模式，找出反复出现的问题根源
        - 摘要：提炼跨会话的核心洞察和用户需求规律
        - 改进策略：形成具体可操作的行为策略，供下次决策参考

        输出格式：Markdown，核心要点以 `-` 开头，保留现有记忆中仍有效的内容，删除过时信息。

        现有 Agent 记忆（MEMORY.md）：
        {existing}

        近期跨会话记忆片段：
        {memories}
        """;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("D-2 DreamingJob 开始执行离线认知整理");
        await RunDreamingAsync(ct);
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
    /// 执行一轮认知整理，供测试直接调用。
    internal async Task RunDreamingAsync(CancellationToken ct)
    {
        IReadOnlyList<AgentConfig> agents = _agentStore.All;
        IReadOnlyList<IMicroSession> allSessions = _repo.GetAll();

        foreach (AgentConfig agent in agents)
        {
            if (ct.IsCancellationRequested) break;
            if (!agent.IsEnabled) continue;

            await DreamForAgentAsync(agent, allSessions, ct);
        }
    }

    private async Task DreamForAgentAsync(
        AgentConfig agent,
        IReadOnlyList<IMicroSession> allSessions,
        CancellationToken ct)
    {
        try
        {
            // 找出与该 Agent 关联的所有 Session
            List<IMicroSession> agentSessions = allSessions
                .Where(s => s.AgentId == agent.Id)
                .ToList();

            if (agentSessions.Count == 0)
            {
                _logger.LogDebug("D-2 Agent={AgentId} 无关联会话，跳过", agent.Id);
                return;
            }

            // 收集近 DailyMemoryLookbackDays 天的日记忆片段
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            var memoryFragments = new List<(string SessionTitle, string Content)>();

            foreach (IMicroSession session in agentSessions)
            {
                for (int daysBack = 1; daysBack <= DailyMemoryLookbackDays; daysBack++)
                {
                    string date = today.AddDays(-daysBack).ToString("yyyy-MM-dd");
                    DailyMemoryInfo? daily = _memoryService.GetDailyMemory(session.Id, date);
                    if (daily is not null && !string.IsNullOrWhiteSpace(daily.Content))
                    {
                        memoryFragments.Add((session.Title, $"[{date}] {daily.Content}"));
                    }
                }
            }

            if (memoryFragments.Count == 0)
            {
                _logger.LogDebug(
                    "D-2 Agent={AgentId} 近 {Days} 天无日记忆片段，跳过认知整理",
                    agent.Id, DailyMemoryLookbackDays);
                return;
            }

            // 选择可用的 LLM Provider
            (ChatMicroProvider? chatProvider, IMicroSession? ownerSession) =
                ResolveChatProvider(agentSessions);
            if (chatProvider is null)
            {
                _logger.LogWarning("D-2 Agent={AgentId} 无可用 Provider，跳过认知整理", agent.Id);
                return;
            }

            // 执行认知整理
            string existingMemory = _agentDnaService.GetMemory(agent.Id);
            MicroChatContext chatCtx = ownerSession is not null
                ? MicroChatContext.ForSystem(ownerSession, "dreaming", ct)
                : MicroChatContext.ForSystem($"agent:{agent.Id}", "dreaming", ct);
            string dreamSummary = await BuildCognitiveDreamAsync(
                existingMemory, memoryFragments, chatProvider, chatCtx);

            if (!string.IsNullOrWhiteSpace(dreamSummary))
            {
                _agentDnaService.UpdateMemory(agent.Id, dreamSummary);
                _logger.LogInformation(
                    "D-2 Agent={AgentId} 认知整理完成，处理了 {SessionCount} 个会话的 {FragmentCount} 个记忆片段",
                    agent.Id, agentSessions.Count, memoryFragments.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "D-2 Agent={AgentId} 认知整理异常", agent.Id);
        }
    }

    /// <summary>
    /// 选择可用的 Chat Provider：
    /// 优先从该 Agent 关联会话的 ProviderId 中找已启用的 Provider，
    /// 否则 fallback 到默认 Chat Provider。返回时一并带出对应 Session 以便构建上下文。
    /// </summary>
    private (ChatMicroProvider? Provider, IMicroSession? OwnerSession) ResolveChatProvider(
        IReadOnlyList<IMicroSession> agentSessions)
    {
        foreach (IMicroSession session in agentSessions)
        {
            ChatMicroProvider? hit = _providerService.TryGetProvider(session.ProviderId);
            if (hit is not null) return (hit, session);
        }

        return (_providerService.GetDefaultProvider(), null);
    }

    // ── 静态 helpers（internal 供测试调用）────────────────────────────────────

    /// <summary>
    /// 调用 LLM 进行跨会话认知归因/摘要整理；返回更新后的 MEMORY.md 完整内容。
    /// </summary>
    internal static async Task<string> BuildCognitiveDreamAsync(
        string existingMemory,
        IReadOnlyList<(string SessionTitle, string Content)> memoryFragments,
        ChatMicroProvider chatProvider,
        MicroChatContext ctx)
    {
        string existing = string.IsNullOrWhiteSpace(existingMemory) ? "（暂无记忆）" : existingMemory;
        string memories = FormatSessionMemories(memoryFragments);
        string prompt = CognitiveDreamPromptTemplate
            .Replace("{existing}", existing)
            .Replace("{memories}", memories);

        ChatResponse response = await chatProvider.ChatAsync(
            ctx,
            [new ChatMessage(ChatRole.User, prompt)]);

        return response.Text ?? string.Empty;
    }

    /// <summary>将会话记忆片段列表格式化为 LLM 可读的结构化文本。</summary>
    internal static string FormatSessionMemories(
        IReadOnlyList<(string SessionTitle, string Content)> memories)
    {
        return string.Join("\n\n", memories.Select(m =>
            $"### 会话「{m.SessionTitle}」\n{m.Content}"));
    }
}
