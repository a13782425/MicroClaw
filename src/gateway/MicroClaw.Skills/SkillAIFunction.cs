using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace MicroClaw.Skills;

/// <summary>
/// 将单个 Skill 技能包装为 AI 可调用的 AIFunction。
/// 参数 Schema 从 workspace/skills/{id}/schema.json 动态读取；
/// 若无 schema.json 则降级为单一 input: string 参数。
/// </summary>
public sealed class SkillAIFunction : AIFunction
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly SkillConfig _skill;
    private readonly SkillRunner _runner;
    private readonly string _workspaceRoot;
    private readonly string _sessionId;
    private readonly JsonElement _jsonSchema;

    public override string Name { get; }
    public override string Description { get; }
    public override JsonElement JsonSchema => _jsonSchema;

    public SkillAIFunction(
        SkillConfig skill,
        SkillService skillService,
        SkillRunner runner,
        string workspaceRoot,
        string sessionId)
    {
        _skill = skill;
        _runner = runner;
        _workspaceRoot = workspaceRoot;
        _sessionId = sessionId;

        Name = SanitizeName(skill.Name);
        Description = skill.Description;
        _jsonSchema = BuildSchema(skillService, skill);
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // 将 arguments 序列化为 JSON 对象传给脚本
        var argsDict = arguments.ToDictionary(kv => kv.Key, kv => kv.Value);
        string argsJson = JsonSerializer.Serialize(argsDict, JsonOpts);

        return await _runner.ExecuteAsync(_skill, argsJson, _workspaceRoot, _sessionId, cancellationToken);
    }

    private static JsonElement BuildSchema(SkillService skillService, SkillConfig skill)
    {
        string? schemaContent = skillService.GetFile(skill.Id, "schema.json");
        if (!string.IsNullOrWhiteSpace(schemaContent))
        {
            try
            {
                JsonElement root = JsonDocument.Parse(schemaContent).RootElement;
                // schema.json 的 parameters 字段即为 OpenAI 风格的参数 schema
                if (root.TryGetProperty("parameters", out JsonElement paramSchema))
                    return paramSchema;
                // 若 schema.json 本身就是参数 schema（有 type=object）
                if (root.TryGetProperty("type", out _))
                    return root;
            }
            catch
            {
                // schema.json 解析失败时降级
            }
        }

        // 降级：单一 input 参数 schema
        const string fallback = """{"type":"object","properties":{"input":{"type":"string","description":"输入内容"}},"required":["input"]}""";
        return JsonDocument.Parse(fallback).RootElement;
    }

    /// <summary>将技能名称处理为合法的 AI 函数名（仅保留字母、数字、下划线）。</summary>
    private static string SanitizeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_');
    }
}

