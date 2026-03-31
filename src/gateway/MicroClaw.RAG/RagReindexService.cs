using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// 全量重索引服务：切换嵌入模型后，将维度不匹配的向量分块重新嵌入。
/// 全局文档从磁盘文件重新嵌入；会话分块读取旧内容，用新模型向量化后写回。
/// </summary>
public sealed class RagReindexService(
    IEmbeddingService embedding,
    IRagService ragService,
    RagDbContextFactory dbFactory,
    ILogger<RagReindexService> logger)
{
    public async Task RunAsync(RagReindexJobTracker tracker, CancellationToken ct = default)
    {
        try
        {
            // 1. 探测当前嵌入维度
            ReadOnlyMemory<float> probe = await embedding.GenerateAsync("test", ct).ConfigureAwait(false);
            int expectedBlobLen = probe.Length * sizeof(float);

            // 2. 统计总任务数
            string[] globalFiles = Directory.Exists(dbFactory.GlobalDocsPath)
                ? Directory.GetFiles(dbFactory.GlobalDocsPath)
                : [];
            IReadOnlyList<string> sessionIds = dbFactory.GetAllSessionIds();
            tracker.Start(globalFiles.Length + sessionIds.Count);

            // 3. 重索引全局文档
            foreach (string filePath in globalFiles)
            {
                ct.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(filePath);
                string sourceId = $"doc:{fileName}";
                tracker.Increment($"全局文档：{fileName}");
                try
                {
                    // 检查是否有维度不匹配的分块
                    bool needsReindex;
                    using (var db = dbFactory.Create(RagScope.Global, null))
                    {
                        needsReindex = await db.VectorChunks.AsNoTracking()
                            .AnyAsync(e => e.SourceId == sourceId && e.VectorBlob.Length != expectedBlobLen, ct)
                            .ConfigureAwait(false);
                    }

                    if (needsReindex)
                    {
                        string content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            await ragService.IngestDocumentAsync(content, fileName, RagScope.Global, null, ct).ConfigureAwait(false);
                            logger.LogInformation("全局文档「{FileName}」重索引完成", fileName);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "全局文档「{FileName}」重索引失败，已跳过", fileName);
                }
            }

            // 4. 重索引会话知识库
            foreach (string sessionId in sessionIds)
            {
                ct.ThrowIfCancellationRequested();
                tracker.Increment($"会话：{sessionId[..Math.Min(8, sessionId.Length)]}…");
                try
                {
                    await ReindexSessionAsync(sessionId, expectedBlobLen, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "会话「{SessionId}」重索引失败，已跳过", sessionId);
                }
            }

            tracker.Complete();
            logger.LogInformation("全量重索引完成，处理 {GlobalCount} 个全局文档 + {SessionCount} 个会话",
                globalFiles.Length, sessionIds.Count);
        }
        catch (OperationCanceledException)
        {
            tracker.Fail("操作已取消");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "全量重索引发生未预期错误");
            tracker.Fail(ex.Message);
        }
    }

    private async Task ReindexSessionAsync(string sessionId, int expectedBlobLen, CancellationToken ct)
    {
        List<VectorChunkEntity> mismatched;
        using (var db = dbFactory.Create(RagScope.Session, sessionId))
        {
            mismatched = await db.VectorChunks.AsNoTracking()
                .Where(e => e.VectorBlob.Length != expectedBlobLen)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        if (mismatched.Count == 0)
            return;

        logger.LogInformation("会话「{SessionId}」发现 {Count} 个维度不匹配分块，开始重新嵌入", sessionId, mismatched.Count);

        // 按 sourceId 分组，批量重新嵌入后写回
        IEnumerable<IGrouping<string, VectorChunkEntity>> groups = mismatched.GroupBy(e => e.SourceId);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            List<VectorChunkEntity> chunks = [.. group];

            // 重新向量化
            IReadOnlyList<ReadOnlyMemory<float>> vectors = await embedding
                .GenerateBatchAsync(chunks.Select(e => e.Content), ct)
                .ConfigureAwait(false);

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using var db = dbFactory.Create(RagScope.Session, sessionId);

            // 删除旧分块
            var oldIds = chunks.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var toDelete = await db.VectorChunks
                .Where(e => oldIds.Contains(e.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            db.VectorChunks.RemoveRange(toDelete);

            // 写入新分块（保留原 SourceId、Content、MetadataJson）
            var newEntities = chunks.Select((chunk, i) => new VectorChunkEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceId = chunk.SourceId,
                Content = chunk.Content,
                VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
                MetadataJson = chunk.MetadataJson,
                CreatedAtMs = nowMs,
            }).ToList();
            db.VectorChunks.AddRange(newEntities);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        logger.LogInformation("会话「{SessionId}」重索引完成，共处理 {Count} 个分块", sessionId, mismatched.Count);
    }
}
