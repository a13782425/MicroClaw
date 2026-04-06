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
/// B-03: 待归纳消息处理后台任务。
/// 每小时运行一次，处理 ContextOverflowSummarizer 写入的 pending 文件：
///   1. 读取溢出消息列表。
///   2. 调用 LLM（Session 绑定的默认模型）生成日摘要。
///   3. 将摘要按主题分类，写入 Session RAG 分类 chunk 并更新 MEMORY.md 目录。
///   4. 删除已处理的 pending 文件。
/// </summary>
public sealed class MemoryPendingProcessorJob(
    ISessionRepository repo,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    MemoryService memoryService,
    IRagService ragService,
    ILogger<MemoryPendingProcessorJob> logger) : IScheduledJob
{
    public string JobName => "memory-pending-processor";
    public JobSchedule Schedule => new JobSchedule.FixedInterval(TimeSpan.FromHours(1), TimeSpan.FromSeconds(90));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await ProcessAllSessionsAsync(ct);
    }

    internal async Task ProcessAllSessionsAsync(CancellationToken ct)
    {
        IReadOnlyList<Session> sessions = repo.GetAll();
        foreach (Session session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessSessionAsync(session, ct);
        }
    }

    private async Task ProcessSessionAsync(Session session, CancellationToken ct)
    {
        IReadOnlyList<string> pendingFiles = memoryService.ListPendingFiles(session.Id);
        if (pendingFiles.Count == 0) return;

        // 获取 LLM 客户端（Session 绑定模型，若不可用则取第一个启用的 Provider）
        ProviderConfig? provider =
            providerStore.All.FirstOrDefault(p => p.Id == session.ProviderId && p.IsEnabled)
            ?? providerStore.All.FirstOrDefault(p => p.IsEnabled);

        if (provider is null)
        {
            logger.LogWarning(
                "B-03 Session={SessionId} 无可用 Provider，跳过 pending 文件处理",
                session.Id);
            return;
        }

        IChatClient client = clientFactory.Create(provider);

        foreach (string fileName in pendingFiles)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessPendingFileAsync(session, fileName, client, ct);
        }
    }

    private async Task ProcessPendingFileAsync(
        Session session, string fileName, IChatClient client, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<SessionMessage> messages = memoryService.ReadPendingMessages(session.Id, fileName);
            if (messages.Count == 0)
            {
                memoryService.DeletePendingFile(session.Id, fileName);
                return;
            }

            // 1. 生成日摘要
            string summary = await MemorySummarizationJob.BuildDailySummaryAsync(messages, client, ct);
            if (string.IsNullOrWhiteSpace(summary))
            {
                logger.LogWarning(
                    "B-03 Session={SessionId} File={File} LLM 返回空摘要，跳过",
                    session.Id, fileName);
                return;
            }

            // 2. 分类归纳：合并到已有分类记忆中
            string existingJson = memoryService.GetCategoriesJson(session.Id);
            string updatedJson = await MemorySummarizationJob.BuildCategoryClassificationAsync(
                existingJson, summary, client, ct);

            if (!string.IsNullOrWhiteSpace(updatedJson))
            {
                Dictionary<string, string>? categories;
                try
                {
                    categories = JsonSerializer.Deserialize<Dictionary<string, string>>(updatedJson);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex,
                        "B-03 Session={SessionId} File={File} 分类 JSON 解析失败",
                        session.Id, fileName);
                    categories = null;
                }

                if (categories is not null && categories.Count > 0)
                {
                    // 3. 写入 RAG 分类 chunk + 更新 MEMORY.md
                    foreach (var (categoryName, content) in categories)
                    {
                        try
                        {
                            await ragService.DeleteBySourceIdAsync(categoryName, RagScope.Session, session.Id, ct);
                            await ragService.IngestAsync(content, categoryName, RagScope.Session, session.Id, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogError(ex,
                                "B-03 Session={SessionId} 分类 '{Category}' 写入 RAG 异常",
                                session.Id, categoryName);
                        }
                    }

                    string newJson = JsonSerializer.Serialize(
                        categories, new JsonSerializerOptions { WriteIndented = true });
                    memoryService.WriteCategoriesJson(session.Id, newJson);
                    memoryService.UpdateLongTermMemory(session.Id, BuildCategoryIndex(categories));

                    logger.LogInformation(
                        "B-03 Session={SessionId} File={File} 分类记忆已更新，共 {Count} 个分类",
                        session.Id, fileName, categories.Count);
                }
            }

            // 4. 删除已处理的 pending 文件
            memoryService.DeletePendingFile(session.Id, fileName);
            logger.LogInformation(
                "B-03 Session={SessionId} 已完成 pending 文件 {File}（{Count} 条消息）",
                session.Id, fileName, messages.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "B-03 Session={SessionId} 处理 pending 文件 {File} 异常",
                session.Id, fileName);
        }
    }

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
}
