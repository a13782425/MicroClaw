using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Skills.Endpoints;

/// <summary>
/// Skill 技能 REST API 端点：技能 CRUD + workspace 文件管理。
/// </summary>
public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 技能 CRUD ────────────────────────────────────────────────────────

        endpoints.MapGet("/skills", (SkillStore store) =>
            Results.Ok(store.All.Select(ToDto)))
            .WithTags("Skills");

        endpoints.MapGet("/skills/{id}", (string id, SkillStore store) =>
        {
            SkillConfig? skill = store.GetById(id);
            return skill is null ? Results.NotFound() : Results.Ok(ToDto(skill));
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills", (SkillCreateRequest req, SkillStore store, SkillService skillService) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { message = "Name is required." });
            if (string.IsNullOrWhiteSpace(req.SkillType))
                return Results.BadRequest(new { message = "SkillType is required." });
            if (string.IsNullOrWhiteSpace(req.EntryPoint))
                return Results.BadRequest(new { message = "EntryPoint is required." });

            SkillConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                Description: req.Description ?? string.Empty,
                SkillType: req.SkillType.ToLowerInvariant(),
                EntryPoint: req.EntryPoint.Trim(),
                IsEnabled: req.IsEnabled,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            SkillConfig created = store.Add(config);
            // 创建 workspace 目录
            skillService.EnsureDirectory(created.Id);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/update", (SkillUpdateRequest req, SkillStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            SkillConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { message = $"Skill '{req.Id}' not found." });

            SkillConfig updated = existing with
            {
                Name = req.Name?.Trim() ?? existing.Name,
                Description = req.Description ?? existing.Description,
                SkillType = req.SkillType?.ToLowerInvariant() ?? existing.SkillType,
                EntryPoint = req.EntryPoint?.Trim() ?? existing.EntryPoint,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
            };

            SkillConfig? result = store.Update(req.Id, updated);
            return result is null ? Results.NotFound() : Results.Ok(new { result.Id });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/delete", (SkillDeleteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            SkillConfig? skill = store.GetById(req.Id);
            if (skill is null)
                return Results.NotFound(new { message = $"Skill '{req.Id}' not found." });

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
                return Results.NotFound(new { message = $"Skill '{id}' not found." });

            return Results.Ok(skillService.ListFiles(id));
        })
        .WithTags("Skills");

        endpoints.MapGet("/skills/{id}/files/{*filePath}", (string id, string filePath, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Skill '{id}' not found." });

            string? content = skillService.GetFile(id, filePath);
            if (content is null)
                return Results.NotFound(new { message = $"File '{filePath}' not found." });

            return Results.Ok(new { content });
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/{id}/files", (string id, SkillFileWriteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Skill '{id}' not found." });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { message = "FileName is required." });

            try
            {
                skillService.WriteFile(id, req.FileName, req.Content ?? string.Empty);
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithTags("Skills");

        endpoints.MapPost("/skills/{id}/files/delete", (string id, SkillFileDeleteRequest req, SkillStore store, SkillService skillService) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Skill '{id}' not found." });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { message = "FileName is required." });

            bool deleted = skillService.DeleteFile(id, req.FileName);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithTags("Skills");

        return endpoints;
    }

    private static object ToDto(SkillConfig s) => new
    {
        s.Id,
        s.Name,
        s.Description,
        s.SkillType,
        s.EntryPoint,
        s.IsEnabled,
        s.CreatedAtUtc,
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record SkillCreateRequest(
    string Name,
    string? Description,
    string SkillType,
    string EntryPoint,
    bool IsEnabled = true);

public sealed record SkillUpdateRequest(
    string Id,
    string? Name = null,
    string? Description = null,
    string? SkillType = null,
    string? EntryPoint = null,
    bool? IsEnabled = null);

public sealed record SkillDeleteRequest(string Id);

public sealed record SkillFileWriteRequest(string FileName, string? Content);

public sealed record SkillFileDeleteRequest(string FileName);
