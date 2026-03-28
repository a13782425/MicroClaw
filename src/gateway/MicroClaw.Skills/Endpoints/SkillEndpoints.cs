using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Skills.Endpoints;

/// <summary>
/// Skill 技能 REST API 端点：技能列表 + workspace 文件管理。
/// Id = 目录名 slug，name/description 统一从 SKILL.md frontmatter 读取。
/// 技能列表由文件系统扫描获得，不依赖数据库。
/// </summary>
public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 技能列表 ─────────────────────────────────────────────────────────

        endpoints.MapGet("/skills", (SkillStore store, SkillService skillService) =>
            Results.Ok(store.All.Select(id => ToDto(id, skillService.ParseManifest(id)))))
            .WithTags("Skills");

        endpoints.MapGet("/skills/{id}", (string id, SkillStore store, SkillService skillService) =>
        {
            if (!store.Exists(id)) return Results.NotFound();
            return Results.Ok(ToDto(id, skillService.ParseManifest(id)));
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/scan", (SkillStore store) =>
        {
            // 纯文件系统扫描，直接返回当前发现的技能数量
            int found = store.All.Count;
            return Results.Ok(new { added = 0, found });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/delete", (SkillDeleteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            if (!store.Exists(req.Id))
                return Results.NotFound(new { success = false, message = $"Skill '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            // 删除 workspace 目录
            skillService.DeleteDirectory(req.Id);
            return Results.Ok();
        })
        .WithTags("Skills");

        // ── 技能文件管理 ──────────────────────────────────────────────────────

        endpoints.MapGet("/skills/{id}/files", (string id, SkillStore store, SkillService skillService) =>
        {
            if (!store.Exists(id))
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(skillService.ListFiles(id));
        })
        .WithTags("Skills");

        endpoints.MapGet("/skills/{id}/files/{*filePath}", (string id, string filePath, SkillStore store, SkillService skillService) =>
        {
            if (!store.Exists(id))
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });

            string? content = skillService.GetFile(id, filePath);
            if (content is null)
                return Results.NotFound(new { success = false, message = $"File '{filePath}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(new { content });
        })
        .WithTags("Skills");

        return endpoints;
    }

    private static object ToDto(string id, SkillManifest manifest) => new
    {
        Id = id,
        Name = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : id,
        Description = manifest.Description,
        manifest.DisableModelInvocation,
        manifest.UserInvocable,
        manifest.AllowedTools,
        manifest.Model,
        manifest.Effort,
        manifest.Context,
        manifest.Agent,
        manifest.ArgumentHint,
        manifest.Hooks,
    };

}


// ── Request records ──────────────────────────────────────────────────────────

public sealed record SkillDeleteRequest(string Id);
