using MicroClaw.Configuration;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent.Memory;
using MicroClaw.RAG;
using MicroClaw.Sessions;

namespace MicroClaw.Endpoints;

public static class RagEndpoints
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /api/rag/global/documents/upload — TODO: Reimplement with MicroRag
        endpoints.MapPost("/rag/global/documents/upload",
            (IFormFile? file) => Results.Ok(new { success = false, message = "RAG 正在重构中" }))
            .DisableAntiforgery()
            .WithTags("RAG");

        // GET /api/rag/global/documents — TODO: Reimplement with MicroRag
        endpoints.MapGet("/rag/global/documents",
            () => Results.Ok(Array.Empty<RagDocumentInfo>()))
            .WithTags("RAG");

        // POST /api/rag/global/documents/delete — TODO: Reimplement with MicroRag
        endpoints.MapPost("/rag/global/documents/delete",
            (DeleteDocumentRequest req) => Results.Ok(new { success = false, message = "RAG 正在重构中" }))
            .WithTags("RAG");

        // POST /api/rag/global/documents/reindex — TODO: Reimplement with MicroRag
        endpoints.MapPost("/rag/global/documents/reindex",
            (ReindexDocumentRequest req) => Results.Ok(new { success = false, message = "RAG 正在重构中" }))
            .WithTags("RAG");

        // GET /api/sessions/{sessionId}/rag/status — TODO: Reimplement with MicroRag
        endpoints.MapGet("/sessions/{sessionId}/rag/status",
            (string sessionId) => Results.Ok(new SessionRagStatusDto(sessionId, 0, null)))
            .WithTags("RAG");

        // GET /api/rag/stats — TODO: Reimplement with MicroRag
        endpoints.MapGet("/rag/stats",
            () => Results.Ok(new RagQueryStats("All", 0, 0, 0, 0, 0, 0)))
            .WithTags("RAG");

        // GET /api/rag/config — 获取 RAG 遗忘配置
        endpoints.MapGet("/rag/config", () =>
            {
                var options = MicroClawConfig.Get<RagOptions>();
                return Results.Ok(new RagConfigDto(options.MaxStorageSizeMb, options.PruneTargetPercent));
            })
            .WithTags("RAG");

        // POST /api/rag/config — 更新 RAG 遗忘配置
        endpoints.MapPost("/rag/config",
            (RagConfigDto req) =>
            {
                if (req.MaxStorageSizeMb <= 0)
                    return Results.BadRequest(new { success = false, message = "maxStorageSizeMb 必须大于 0。", errorCode = "BAD_REQUEST" });
                if (req.PruneTargetPercent is <= 0 or > 1)
                    return Results.BadRequest(new { success = false, message = "pruneTargetPercent 必须在 (0, 1] 之间。", errorCode = "BAD_REQUEST" });

                var updated = new RagOptions
                {
                    MaxStorageSizeMb = req.MaxStorageSizeMb,
                    PruneTargetPercent = req.PruneTargetPercent,
                };
                MicroClawConfig.Update(updated);

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
                catch { }

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

        // POST /api/sessions/{sessionId}/rag/vectorize — 手动将会话全部消息写入 pending JSONL 等待向量化，并从活跃历史中移除
        endpoints.MapPost("/sessions/{sessionId}/rag/vectorize",
            (string sessionId, ISessionService sessionRepository, MemoryService memoryService) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return Results.BadRequest(new { success = false, message = "sessionId 不能为空。", errorCode = "BAD_REQUEST" });

                var allMessages = sessionRepository.GetMessages(sessionId);
                if (allMessages.Count == 0)
                    return Results.BadRequest(new { success = false, message = "该会话暂无消息。", errorCode = "NO_MESSAGES" });

                // 1. 写入 pending JSONL
                string pendingFile = memoryService.WritePendingMessages(sessionId, allMessages);

                // 2. 从 messages.jsonl 中移除已归档的消息
                var ids = allMessages.Select(m => m.Id).ToHashSet();
                sessionRepository.RemoveMessages(sessionId, ids);

                return Results.Ok(new { success = true, messageCount = allMessages.Count, pendingFile });
            })
            .WithTags("RAG");

        // ── RAG Chunk 管理端点 ──────────────────────────────────────────────

        // GET /api/sessions/{sessionId}/rag/chunks — TODO: Reimplement with MicroRag
        endpoints.MapGet("/sessions/{sessionId}/rag/chunks",
            (string sessionId) => Results.Ok(new { chunks = Array.Empty<RagChunkInfo>() }))
            .WithTags("RAG");

        // GET /api/rag/global/chunks — TODO: Reimplement with MicroRag
        endpoints.MapGet("/rag/global/chunks",
            () => Results.Ok(new { chunks = Array.Empty<RagChunkInfo>() }))
            .WithTags("RAG");

        // DELETE /api/rag/chunks/{chunkId} — TODO: Reimplement with MicroRag
        endpoints.MapDelete("/rag/chunks/{chunkId}",
            (string chunkId) => Results.Ok(new { success = false, message = "RAG 正在重构中" }))
            .WithTags("RAG");

        // POST /api/rag/chunks/{chunkId}/hit-count — TODO: Reimplement with MicroRag
        endpoints.MapPost("/rag/chunks/{chunkId}/hit-count",
            (string chunkId, UpdateHitCountRequest req) => Results.Ok(new { success = false, message = "RAG 正在重构中" }))
            .WithTags("RAG");

        return endpoints;
    }

    private sealed record DeleteDocumentRequest(string SourceId);
    private sealed record ReindexDocumentRequest(string SourceId);
    private sealed record UpdateHitCountRequest(int HitCount);
    internal sealed record SessionRagStatusDto(string SessionId, int CategoryCount, long? LastUpdatedAtMs);
    internal sealed record RagConfigDto(double MaxStorageSizeMb, double PruneTargetPercent);
}
