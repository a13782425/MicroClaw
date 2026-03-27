using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Skills.Endpoints;

/// <summary>
/// Skill 技能 REST API 端点：技能 CRUD + workspace 文件管理。
/// Id = 目录名 slug，name/description 统一从 SKILL.md frontmatter 读取。
/// </summary>
public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 技能 CRUD ────────────────────────────────────────────────────────

        endpoints.MapGet("/skills", (SkillStore store, SkillService skillService) =>
            Results.Ok(store.All.Select(s => ToDto(s, skillService.ParseManifest(s.Id)))))
            .WithTags("Skills");

        endpoints.MapGet("/skills/{id}", (string id, SkillStore store, SkillService skillService) =>
        {
            SkillConfig? skill = store.GetById(id);
            return skill is null ? Results.NotFound() : Results.Ok(ToDto(skill, skillService.ParseManifest(id)));
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/update", (SkillUpdateRequest req, SkillStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            SkillConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Skill '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            SkillConfig? result = store.Update(req.Id, req.IsEnabled ?? existing.IsEnabled);
            return result is null ? Results.NotFound() : Results.Ok(new { result.Id });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/scan", (SkillStore store, SkillService skillService) =>
        {
            int found = 0, added = 0;
            foreach (string root in skillService.SkillRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string skillMdPath = Path.Combine(dir, "SKILL.md");
                    if (!File.Exists(skillMdPath)) continue;

                    found++;
                    string dirName = Path.GetFileName(dir);
                    if (store.Exists(dirName)) continue;

                    SkillConfig config = new(
                        Id: dirName,
                        IsEnabled: true,
                        CreatedAtUtc: DateTimeOffset.UtcNow);
                    store.Add(config);
                    added++;
                }
            }

            return Results.Ok(new { added, found });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/delete", (SkillDeleteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            SkillConfig? skill = store.GetById(req.Id);
            if (skill is null)
                return Results.NotFound(new { success = false, message = $"Skill '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            store.Delete(req.Id);
            // 删除 workspace 目录
            skillService.DeleteDirectory(req.Id);
            return Results.Ok();
        })
        .WithTags("Skills");

        // ── 技能文件管理 ──────────────────────────────────────────────────────

        endpoints.MapGet("/skills/{id}/files", (string id, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(skillService.ListFiles(id));
        })
        .WithTags("Skills");

        endpoints.MapGet("/skills/{id}/files/{*filePath}", (string id, string filePath, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });

            string? content = skillService.GetFile(id, filePath);
            if (content is null)
                return Results.NotFound(new { success = false, message = $"File '{filePath}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(new { content });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/{id}/files", (string id, SkillFileWriteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

            try
            {
                skillService.WriteFile(id, req.FileName, req.Content ?? string.Empty);
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "BAD_REQUEST" });
            }
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/{id}/files/delete", (string id, SkillFileDeleteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Skill '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

            bool deleted = skillService.DeleteFile(id, req.FileName);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithTags("Skills");

        return endpoints;
    }

    private static object ToDto(SkillConfig s, SkillManifest manifest) => new
    {
        s.Id,
        Name = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : s.Id,
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
        s.IsEnabled,
        s.CreatedAtUtc,
    };

}


// ── Request records ──────────────────────────────────────────────────────────

public sealed record SkillUpdateRequest(
    string Id,
    bool? IsEnabled = null);

public sealed record SkillDeleteRequest(string Id);

public sealed record SkillFileWriteRequest(string FileName, string? Content);

public sealed record SkillFileDeleteRequest(string FileName);
