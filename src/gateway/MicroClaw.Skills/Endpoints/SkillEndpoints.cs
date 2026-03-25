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

        endpoints.MapPost("/skills", (SkillCreateRequest req, SkillStore store, SkillService skillService) =>
        {
            if (string.IsNullOrWhiteSpace(req.Slug))
                return Results.BadRequest(new { success = false, message = "Slug is required.", errorCode = "BAD_REQUEST" });

            string slug = req.Slug.Trim().ToLowerInvariant();
            if (!SkillStore.IsValidSlug(slug))
                return Results.BadRequest(new { success = false, message = "Slug must be 1-64 chars, lowercase letters/digits/hyphens, start and end with letter or digit.", errorCode = "BAD_REQUEST" });

            if (store.Exists(slug))
                return Results.BadRequest(new { success = false, message = $"Skill '{slug}' already exists.", errorCode = "CONFLICT" });

            SkillConfig config = new(
                Id: slug,
                IsEnabled: req.IsEnabled,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            SkillConfig created = store.Add(config);
            skillService.EnsureDirectory(created.Id);
            GenerateSkillMd(skillService, created.Id, req.Name, req.Description);
            return Results.Ok(new { created.Id });
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
            string skillsRoot = Path.Combine(skillService.WorkspaceRoot, "skills");
            if (!Directory.Exists(skillsRoot))
                return Results.Ok(new { added = 0, found = 0 });

            int found = 0, added = 0;
            foreach (string dir in Directory.GetDirectories(skillsRoot))
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

    /// <summary>
    /// 创建技能时生成标准 SKILL.md 模板文件。
    /// 遵循 Agent Skills 开放标准（agentskills.io）frontmatter 格式。
    /// </summary>
    private static void GenerateSkillMd(SkillService skillService, string skillId, string? name, string? description)
    {
        string displayName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : skillId;
        string desc = description?.Trim() ?? string.Empty;
        string skillMd = $"""
---
name: {skillId}
description: {desc}
argument-hint: "[input]"
disable-model-invocation: false
user-invocable: true
---

# {displayName}

{desc}

<!-- 在此处编写技能的详细指令。Claude 将在执行此技能时遵循这些指令。 -->
""";
        skillService.WriteFile(skillId, "SKILL.md", skillMd);
    }
}


// ── Request records ──────────────────────────────────────────────────────────

public sealed record SkillCreateRequest(
    string Slug,
    string? Name = null,
    string? Description = null,
    bool IsEnabled = true);

public sealed record SkillUpdateRequest(
    string Id,
    bool? IsEnabled = null);

public sealed record SkillDeleteRequest(string Id);

public sealed record SkillFileWriteRequest(string FileName, string? Content);

public sealed record SkillFileDeleteRequest(string FileName);
