using MicroClaw.Agent.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Agent.Endpoints;

/// <summary>
/// 全局 DNA 端点：管理 {workspace}/dna/ 目录下的共享记忆文件（三层 DNA 架构第一层）。
/// 路由前缀 /api/dna，需要认证。
/// </summary>
public static class GlobalDnaEndpoints
{
    public static IEndpointRouteBuilder MapGlobalDnaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 列表 ─────────────────────────────────────────────────────────────

        endpoints.MapGet("/dna", (DNAService dna) =>
            Results.Ok(dna.ListGlobal()))
            .WithTags("GlobalDNA");

        // ── 写入/更新 ─────────────────────────────────────────────────────────

        endpoints.MapPost("/dna", (GeneFileWriteRequest req, DNAService dna) =>
        {
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(req.FileName);
            string safeCategory = SanitizeCategory(req.Category);

            GeneFile file = dna.WriteGlobal(safeCategory, safeName, req.Content ?? string.Empty);
            return Results.Ok(file);
        })
        .WithTags("GlobalDNA");

        // ── 删除 ─────────────────────────────────────────────────────────────

        endpoints.MapPost("/dna/delete", (GeneFileDeleteRequest req, DNAService dna) =>
        {
            string safeName = Path.GetFileName(req.FileName ?? string.Empty);
            bool deleted = dna.DeleteGlobal(SanitizeCategory(req.Category), safeName);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithTags("GlobalDNA");

        // ── 快照列表 ─────────────────────────────────────────────────────────

        endpoints.MapGet("/dna/snapshots", (string fileName, string? category, DNAService dna) =>
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { success = false, message = "fileName query parameter is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(fileName);
            string safeCategory = SanitizeCategory(category);
            return Results.Ok(dna.ListGlobalSnapshots(safeCategory, safeName));
        })
        .WithTags("GlobalDNA");

        // ── 还原快照 ─────────────────────────────────────────────────────────

        endpoints.MapPost("/dna/restore", (GeneFileRestoreRequest req, DNAService dna) =>
        {
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.SnapshotId))
                return Results.BadRequest(new { success = false, message = "SnapshotId is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(req.FileName);
            string safeCategory = SanitizeCategory(req.Category);

            try
            {
                GeneFile restored = dna.RestoreGlobalSnapshot(safeCategory, safeName, req.SnapshotId);
                return Results.Ok(restored);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { success = false, message = ex.Message, errorCode = "SNAPSHOT_NOT_FOUND" });
            }
        })
        .WithTags("GlobalDNA");

        // ── 导出 Markdown ─────────────────────────────────────────────────────

        endpoints.MapGet("/dna/export", (DNAService dna) =>
        {
            string markdown = dna.ExportGlobalToMarkdown();
            return Results.Text(markdown, "text/plain; charset=utf-8");
        })
        .WithTags("GlobalDNA");

        // ── 导入 Markdown ─────────────────────────────────────────────────────

        endpoints.MapPost("/dna/import", (DnaMarkdownImportRequest req, DNAService dna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content))
                return Results.BadRequest(new { success = false, message = "Content is required.", errorCode = "BAD_REQUEST" });

            IReadOnlyList<DnaImportEntryResult> entries = dna.ImportGlobalFromMarkdown(req.Content);
            return Results.Ok(new { imported = entries.Count(r => r.Success), total = entries.Count, entries });
        })
        .WithTags("GlobalDNA");

        return endpoints;
    }

    /// <summary>净化 category 路径段，防止路径穿越攻击。</summary>
    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        return string.Join("/",
            category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
