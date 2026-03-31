using MicroClaw.Configuration;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.RAG;
using MicroClaw.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Endpoints;

public static class RagEndpoints
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /api/rag/global/documents/upload — 上传文档（TXT/MD）
        endpoints.MapPost("/rag/global/documents/upload",
            async (IFormFile file, IRagService ragService, RagDbContextFactory dbFactory, CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { success = false, message = "请选择要上传的文件。", errorCode = "BAD_REQUEST" });

                var ext = Path.GetExtension(file.FileName);
                if (!AllowedExtensions.Contains(ext))
                    return Results.BadRequest(new { success = false, message = "仅支持 .txt 和 .md 格式的文档。", errorCode = "UNSUPPORTED_TYPE" });

                // 文件名净化：仅保留文件名部分，不允许路径穿越
                var safeFileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrEmpty(safeFileName))
                    return Results.BadRequest(new { success = false, message = "文件名无效。", errorCode = "BAD_REQUEST" });

                // 读取文件内容
                string content;
                using (var reader = new StreamReader(file.OpenReadStream()))
                    content = await reader.ReadToEndAsync(ct);

                if (string.IsNullOrWhiteSpace(content))
                    return Results.BadRequest(new { success = false, message = "文件内容为空。", errorCode = "BAD_REQUEST" });

                // 持久化到 global_docs 目录
                var docsDir = dbFactory.GlobalDocsPath;
                Directory.CreateDirectory(docsDir);
                var filePath = Path.Combine(docsDir, safeFileName);
                await File.WriteAllTextAsync(filePath, content, ct);

                // 索引（同名文档自动重索引）
                var sourceId = await ragService.IngestDocumentAsync(content, safeFileName, RagScope.Global, null, ct);

                // 返回文档信息
                var docs = await ragService.ListDocumentsAsync(RagScope.Global, null, ct);
                var doc = docs.FirstOrDefault(d => d.SourceId == sourceId);

                return Results.Ok(new
                {
                    success = true,
                    sourceId,
                    fileName = safeFileName,
                    chunkCount = doc?.ChunkCount ?? 0,
                });
            })
            .DisableAntiforgery()
            .WithTags("RAG");

        // GET /api/rag/global/documents — 列出所有已索引文档
        endpoints.MapGet("/rag/global/documents",
            async (IRagService ragService, CancellationToken ct) =>
            {
                var docs = await ragService.ListDocumentsAsync(RagScope.Global, null, ct);
                return Results.Ok(docs);
            })
            .WithTags("RAG");

        // POST /api/rag/global/documents/delete — 删除文档（DB 分块 + 磁盘文件）
        endpoints.MapPost("/rag/global/documents/delete",
            async (DeleteDocumentRequest req, IRagService ragService, RagDbContextFactory dbFactory, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.SourceId))
                    return Results.BadRequest(new { success = false, message = "sourceId 不能为空。", errorCode = "BAD_REQUEST" });

                if (!req.SourceId.StartsWith("doc:", StringComparison.Ordinal))
                    return Results.BadRequest(new { success = false, message = "只能删除通过上传添加的文档（doc: 前缀）。", errorCode = "BAD_REQUEST" });

                // 删除 DB 分块
                await ragService.DeleteBySourceIdAsync(req.SourceId, RagScope.Global, null, ct);

                // 删除磁盘文件（文件不存在时忽略）
                var fileName = req.SourceId["doc:".Length..];
                var safeFileName = Path.GetFileName(fileName);
                if (!string.IsNullOrEmpty(safeFileName))
                {
                    var filePath = Path.Combine(dbFactory.GlobalDocsPath, safeFileName);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }

                return Results.Ok(new { success = true });
            })
            .WithTags("RAG");

        // POST /api/rag/global/documents/reindex — 从磁盘重新生成嵌入（用于模型切换后刷新）
        endpoints.MapPost("/rag/global/documents/reindex",
            async (ReindexDocumentRequest req, IRagService ragService, RagDbContextFactory dbFactory, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.SourceId))
                    return Results.BadRequest(new { success = false, message = "sourceId 不能为空。", errorCode = "BAD_REQUEST" });

                if (!req.SourceId.StartsWith("doc:", StringComparison.Ordinal))
                    return Results.BadRequest(new { success = false, message = "只能重索引通过上传添加的文档（doc: 前缀）。", errorCode = "BAD_REQUEST" });

                var fileName = req.SourceId["doc:".Length..];
                var safeFileName = Path.GetFileName(fileName);
                if (string.IsNullOrEmpty(safeFileName))
                    return Results.BadRequest(new { success = false, message = "SourceId 格式不正确。", errorCode = "BAD_REQUEST" });

                var filePath = Path.Combine(dbFactory.GlobalDocsPath, safeFileName);
                if (!File.Exists(filePath))
                    return Results.NotFound(new { success = false, message = "磁盘上找不到对应的文档文件，请重新上传。", errorCode = "NOT_FOUND" });

                var content = await File.ReadAllTextAsync(filePath, ct);
                if (string.IsNullOrWhiteSpace(content))
                    return Results.BadRequest(new { success = false, message = "文档内容为空，无法重索引。", errorCode = "BAD_REQUEST" });

                // IngestDocumentAsync 内部自动删除旧分块后重新索引
                await ragService.IngestDocumentAsync(content, safeFileName, RagScope.Global, null, ct);

                var docs = await ragService.ListDocumentsAsync(RagScope.Global, null, ct);
                var doc = docs.FirstOrDefault(d => d.SourceId == req.SourceId);

                return Results.Ok(new
                {
                    success = true,
                    sourceId = req.SourceId,
                    chunkCount = doc?.ChunkCount ?? 0,
                });
            })
            .WithTags("RAG");

        // GET /api/sessions/{sessionId}/rag/status — 查询会话RAG分类数据状态
        endpoints.MapGet("/sessions/{sessionId}/rag/status",
            async (string sessionId, RagDbContextFactory dbFactory, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return Results.BadRequest(new { success = false, message = "sessionId 不能为空。", errorCode = "BAD_REQUEST" });

                try
                {
                    using var db = dbFactory.Create(RagScope.Session, sessionId);
                    var categoryChunks = await db.VectorChunks.AsNoTracking()
                        .Where(e => !e.SourceId.StartsWith("doc:"))
                        .Select(e => new { e.SourceId, e.CreatedAtMs })
                        .ToListAsync(ct);

                    int categoryCount = categoryChunks.Select(e => e.SourceId).Distinct().Count();
                    long? lastUpdatedAtMs = categoryChunks.Count > 0
                        ? categoryChunks.Max(e => e.CreatedAtMs)
                        : null;

                    return Results.Ok(new SessionRagStatusDto(sessionId, categoryCount, lastUpdatedAtMs));
                }
                catch
                {
                    return Results.Ok(new SessionRagStatusDto(sessionId, 0, null));
                }
            })
            .WithTags("RAG");

        // GET /api/rag/stats — 查询 RAG 检索聚合统计
        endpoints.MapGet("/rag/stats",
            async (IRagService ragService, string? scope, CancellationToken ct) =>
            {
                RagScope? ragScope = null;
                if (!string.IsNullOrWhiteSpace(scope))
                {
                    if (!Enum.TryParse<RagScope>(scope, ignoreCase: true, out var parsed))
                        return Results.BadRequest(new { success = false, message = "scope 参数无效，可选值：Global、Session。", errorCode = "BAD_REQUEST" });
                    ragScope = parsed;
                }

                var stats = await ragService.GetQueryStatsAsync(ragScope, ct);
                return Results.Ok(stats);
            })
            .WithTags("RAG");

        // GET /api/rag/config — 获取 RAG 遗忘配置
        endpoints.MapGet("/rag/config", () =>
            {
                var options = MicroClawConfig.Get<RagOptions>();
                return Results.Ok(new RagConfigDto(options.MaxStorageSizeMb, options.PruneTargetPercent));
            })
            .WithTags("RAG");

        // POST /api/rag/config — 更新 RAG 遗忘配置（持久化到 YAML + 热更新内存）
        endpoints.MapPost("/rag/config",
            (RagConfigDto req, IRagPruner pruner) =>
            {
                if (req.MaxStorageSizeMb <= 0)
                    return Results.BadRequest(new { success = false, message = "maxStorageSizeMb 必须大于 0。", errorCode = "BAD_REQUEST" });
                if (req.PruneTargetPercent is <= 0 or > 1)
                    return Results.BadRequest(new { success = false, message = "pruneTargetPercent 必须在 (0, 1] 之间。", errorCode = "BAD_REQUEST" });

                // 1. Update in-memory config
                var updated = new RagOptions
                {
                    MaxStorageSizeMb = req.MaxStorageSizeMb,
                    PruneTargetPercent = req.PruneTargetPercent,
                };
                MicroClawConfig.Update(updated);

                // 2. Update RagPruner thresholds at runtime
                if (pruner is RagPruner concretePruner)
                    concretePruner.UpdateThresholds(req.MaxStorageSizeMb, req.PruneTargetPercent);

                // 3. Persist to YAML (write to {home}/config/rag.yaml for $imports pickup)
                try
                {
                    string configDir = Path.Combine(MicroClawConfig.Env.Home, "config");
                    Directory.CreateDirectory(configDir);
                    string yamlContent = $"""
                                          rag:
                                            maxStorageSizeMb: {req.MaxStorageSizeMb}
                                            pruneTargetPercent: {req.PruneTargetPercent}
                                          """;
                    File.WriteAllText(Path.Combine(configDir, "rag.yaml"), yamlContent);
                }
                catch
                {
                    // Config update already applied in memory; YAML persistence failure is non-fatal
                }

                return Results.Ok(new { success = true, maxStorageSizeMb = req.MaxStorageSizeMb, pruneTargetPercent = req.PruneTargetPercent });
            })
            .WithTags("RAG");

        // POST /api/rag/reindex-all
        endpoints.MapPost("/rag/reindex-all",
            (RagReindexJobTracker tracker, RagReindexService reindexSvc) =>
            {
                if (tracker.Status == ReindexJobStatus.Running)
                    return Results.Conflict(new { success = false, message = "重索引任务正在进行中，请稍候。" });

                tracker.Reset();
                _ = Task.Run(() => reindexSvc.RunAsync(tracker));
                return Results.Ok(new { started = true });
            })
            .WithTags("RAG");

        // GET /api/rag/reindex-all/status
        endpoints.MapGet("/rag/reindex-all/status",
            (RagReindexJobTracker tracker) => Results.Ok(new
            {
                status = tracker.Status.ToString().ToLowerInvariant(),
                total = tracker.Total,
                completed = tracker.Completed,
                currentItem = tracker.CurrentItem,
                error = tracker.Error,
            }))
            .WithTags("RAG");

        return endpoints;
    }

    private sealed record DeleteDocumentRequest(string SourceId);
    private sealed record ReindexDocumentRequest(string SourceId);
    internal sealed record SessionRagStatusDto(string SessionId, int CategoryCount, long? LastUpdatedAtMs);
    internal sealed record RagConfigDto(double MaxStorageSizeMb, double PruneTargetPercent);
}
