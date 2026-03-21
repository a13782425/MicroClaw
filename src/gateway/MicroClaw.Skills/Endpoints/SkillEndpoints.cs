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
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.SkillType))
                return Results.BadRequest(new { success = false, message = "SkillType is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.EntryPoint))
                return Results.BadRequest(new { success = false, message = "EntryPoint is required.", errorCode = "BAD_REQUEST" });

            SkillConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                Description: req.Description ?? string.Empty,
                SkillType: req.SkillType.ToLowerInvariant(),
                EntryPoint: req.EntryPoint.Trim(),
                IsEnabled: req.IsEnabled,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TimeoutSeconds: req.TimeoutSeconds);

            SkillConfig created = store.Add(config);
            // 创建 workspace 目录并生成模板文件
            skillService.EnsureDirectory(created.Id);
            GenerateTemplateFiles(skillService, created);
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

            SkillConfig updated = existing with
            {
                Name = req.Name?.Trim() ?? existing.Name,
                Description = req.Description ?? existing.Description,
                SkillType = req.SkillType?.ToLowerInvariant() ?? existing.SkillType,
                EntryPoint = req.EntryPoint?.Trim() ?? existing.EntryPoint,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                TimeoutSeconds = req.TimeoutSeconds ?? existing.TimeoutSeconds,
            };

            SkillConfig? result = store.Update(req.Id, updated);
            return result is null ? Results.NotFound() : Results.Ok(new { result.Id });
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

    private static object ToDto(SkillConfig s) => new
    {
        s.Id,
        s.Name,
        s.Description,
        s.SkillType,
        s.EntryPoint,
        s.IsEnabled,
        s.CreatedAtUtc,
        s.TimeoutSeconds,
    };

    /// <summary>
    /// 创建技能时自动生成模板文件：入口脚本、schema.json 参数定义、skill.md 说明文档。
    /// </summary>
    private static void GenerateTemplateFiles(SkillService skillService, SkillConfig skill)
    {
        // skill.md —— 接口约定说明，让 AI 和开发者都能理解调用规约
        string skillMd = $"""
# {skill.Name}

{skill.Description}

## 调用规约

- **输入**：通过 `stdin` 接收一行 JSON，字段由 `schema.json` 定义。
- **输出**：将结果字符串写入 `stdout`（最后一行视为返回值）。
- **错误**：写入 `stderr`，非 0 退出码视为执行失败。

## 环境变量

| 变量 | 说明 |
|------|------|
| `MICROCLAW_SESSION_ID` | 当前会话 ID |
| `MICROCLAW_SKILL_DIR` | 本技能目录绝对路径 |
| `MICROCLAW_WORKSPACE_DIR` | Workspace 根目录路径 |

## 参数 Schema

参数在 `schema.json` 中定义（OpenAI Function Calling 格式）。

## 文件说明

| 文件 | 说明 |
|------|------|
| `{skill.EntryPoint}` | 入口脚本（由 Agent 调用） |
| `schema.json` | 参数定义（JSON Schema） |
| `skill.md` | 本说明文档 |
""";
        skillService.WriteFile(skill.Id, "SKILL.md", skillMd);

        // schema.json —— 默认单一 input 参数，开发者可按需扩展
        const string schemaJson = """
{
  "type": "object",
  "properties": {
    "input": {
      "type": "string",
      "description": "输入内容"
    }
  },
  "required": ["input"]
}
""";
        skillService.WriteFile(skill.Id, "schema.json", schemaJson);

        // 入口脚本模板（按 SkillType 生成）
        // 仅当入口文件尚不存在时才生成，避免覆盖用户已手写的脚本
        if (skillService.GetFile(skill.Id, skill.EntryPoint) is null)
        {
            string entryContent = skill.SkillType switch
            {
                "python" => PythonTemplate(skill),
                "nodejs" or "node" => NodejsTemplate(skill),
                "shell" or "bash" or "sh" => ShellTemplate(skill),
                _ => string.Empty,
            };
            if (!string.IsNullOrEmpty(entryContent))
                skillService.WriteFile(skill.Id, skill.EntryPoint, entryContent);
        }
    }

    private static string PythonTemplate(SkillConfig skill)
    {
        // 用4-quote delimiter，允许 Python 三引号 """ 出现在内容中
        string header = $""""
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
{skill.Name} — {skill.Description}

调用规约：
  stdin : 一行 JSON，包含 schema.json 定义的参数。
  stdout: 返回结果字符串（整个 stdout 会被采集）。
  stderr: 错误信息（不作为返回值）。
"""
"""";
        // 静态 body 用非插值 raw string，避免 Python f-string 花括号触发 CS9006
        const string body = """

import sys
import json
import os

# ── 读取参数 ────────────────────────────────────────────────
args = json.loads(sys.stdin.readline())
input_text = args.get("input", "")

# ── 可用的环境变量 ──────────────────────────────────────────
session_id    = os.environ.get("MICROCLAW_SESSION_ID", "")
skill_dir     = os.environ.get("MICROCLAW_SKILL_DIR", "")
workspace_dir = os.environ.get("MICROCLAW_WORKSPACE_DIR", "")

# ── 技能逻辑（在此实现）─────────────────────────────────────
result = f"收到输入：{input_text}"

# ── 输出结果 ────────────────────────────────────────────────
print(result)
""";
        return header + body;
    }

    private static string NodejsTemplate(SkillConfig skill)
    {
        string header = $"""
#!/usr/bin/env node
/**
 * {skill.Name} — {skill.Description}
 *
 * 调用规约：
 *   stdin : 一行 JSON，包含 schema.json 定义的参数。
 *   stdout: 返回结果字符串。
 *   stderr: 错误信息。
 */
""";
        // 静态 body 用非插值 raw string，避免 JS ${...} 花括号触发 CS9006
        const string body = """

let rawInput = '';
process.stdin.on('data', chunk => (rawInput += chunk));
process.stdin.on('end', () => {
  const args = JSON.parse(rawInput);
  const input = args.input ?? '';

  // 可用环境变量
  const sessionId    = process.env.MICROCLAW_SESSION_ID    ?? '';
  const skillDir     = process.env.MICROCLAW_SKILL_DIR     ?? '';
  const workspaceDir = process.env.MICROCLAW_WORKSPACE_DIR ?? '';

  // 技能逻辑（在此实现）
  const result = `收到输入：${input}`;

  process.stdout.write(result + '\n');
});
""";
        return header + body;
    }

    private static string ShellTemplate(SkillConfig skill)
    {
        string header = $"""
#!/usr/bin/env bash
# {skill.Name} — {skill.Description}
#
# 调用规约：
#   stdin : 一行 JSON（可用 jq 解析）
#   stdout: 返回结果字符串
#   stderr: 错误信息

""";
        // 静态 body 用非插值 raw string，避免 Shell ${VAR} 花括号触发 CS9006
        const string body = """
set -euo pipefail

# 读取参数（需要系统安装 jq）
INPUT_JSON=$(cat)
INPUT=$(echo "$INPUT_JSON" | jq -r '.input // ""')

# 可用环境变量
SESSION_ID="${MICROCLAW_SESSION_ID:-}"
SKILL_DIR="${MICROCLAW_SKILL_DIR:-}"
WORKSPACE_DIR="${MICROCLAW_WORKSPACE_DIR:-}"

# 技能逻辑（在此实现）
echo "收到输入：$INPUT"
""";
        return header + body;
    }
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record SkillCreateRequest(
    string Name,
    string? Description,
    string SkillType,
    string EntryPoint,
    bool IsEnabled = true,
    int TimeoutSeconds = 30);

public sealed record SkillUpdateRequest(
    string Id,
    string? Name = null,
    string? Description = null,
    string? SkillType = null,
    string? EntryPoint = null,
    bool? IsEnabled = null,
    int? TimeoutSeconds = null);

public sealed record SkillDeleteRequest(string Id);

public sealed record SkillFileWriteRequest(string FileName, string? Content);

public sealed record SkillFileDeleteRequest(string FileName);
