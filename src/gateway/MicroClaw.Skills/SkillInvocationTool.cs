using System.ComponentModel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Skills;

/// <summary>
/// 技能调用工具（invoke_skill）：注册为 AIFunction，供 Claude 在 ReAct 循环中按需加载技能全文指令。
/// 实现官方 Agent Skills 懒加载模型：System Prompt 中只有描述目录，全文在调用此工具时才注入。
/// 支持 context:fork（将技能在隔离子 Agent 中执行）和 hooks（on-invoke / on-complete）。
/// </summary>
public sealed class SkillInvocationTool
{
    private readonly SkillToolFactory _factory;
    private readonly SkillService _skillService;
    private readonly SkillOptions _options;
    private readonly ISubAgentRunner? _subAgentRunner;
    private readonly IAgentLookup? _agentLookup;
    private readonly ILogger<SkillInvocationTool> _logger;

    public SkillInvocationTool(IServiceProvider sp)
    {
        _factory = sp.GetRequiredService<SkillToolFactory>();
        _skillService = sp.GetRequiredService<SkillService>();
        _options = MicroClawConfig.Get<SkillOptions>();
        _logger = sp.GetRequiredService<ILogger<SkillInvocationTool>>();
        _subAgentRunner = sp.GetService<ISubAgentRunner>();
        _agentLookup = sp.GetService<IAgentLookup>();
    }

    /// <summary>
    /// 为指定绑定技能集创建 invoke_skill AIFunction 实例。
    /// </summary>
    public AIFunction Create(IReadOnlyList<string> boundSkillIds, string? sessionId)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("要调用的技能名称（SKILL.md 中的 name 字段，即 /slash-command 名）")] string name,
                [Description("传递给技能的参数（可选），对应 $ARGUMENTS 占位符")] string? arguments,
                CancellationToken ct) =>
            {
                return await InvokeAsync(boundSkillIds, sessionId, name, arguments, ct);
            },
            name: "invoke_skill",
            description: "调用已绑定的技能，加载其完整指令并按指令执行任务。技能名称来自 /skills 目录列表。");
    }

    // ── 核心调用逻辑 ────────────────────────────────────────────────────────

    private async Task<object> InvokeAsync(
        IReadOnlyList<string> boundSkillIds,
        string? sessionId,
        string skillName,
        string? arguments,
        CancellationToken ct)
    {
        var resolved = _factory.ResolveSkill(boundSkillIds, skillName);
        if (resolved is null)
            return new { success = false, error = $"技能 '{skillName}' 未找到或未启用。" };

        var (skillId, manifest) = resolved.Value;

        // ── on-invoke hooks ────────────────────────────────────────────────
        HookExecutionResult hookResult = await ExecuteHooksAsync(manifest.ParsedHooks.OnInvoke, skillId, "on-invoke");
        if (!hookResult.Success)
            return new { success = false, error = hookResult.Error };

        object result;
        try
        {
            if (string.Equals(manifest.Context, "fork", StringComparison.OrdinalIgnoreCase))
            {
                result = await ForkInvokeAsync(skillId, manifest, sessionId, arguments, ct);
            }
            else
            {
                result = InlineInvoke(skillId, manifest, sessionId, arguments);
            }
        }
        finally
        {
            // ── on-complete hooks（fire-and-forget，不阻塞返回）─────────────
            _ = ExecuteHooksAsync(manifest.ParsedHooks.OnComplete, skillId, "on-complete");
        }

        return result;
    }

    // ── Inline 执行（非 fork） ───────────────────────────────────────────────

    private object InlineInvoke(
        string skillId,
        SkillManifest manifest,
        string? sessionId,
        string? arguments)
    {
        string instructions = _factory.BuildSkillInstructionsFromManifest(
            skillId, manifest, sessionId, arguments);

        // 列出附加文件（除 SKILL.md 外），供 AI 通过 read_skill_file 按需读取
        var files = _skillService.ListFiles(skillId)
            .Where(f => !f.Path.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .ToList();

        if (files.Count > 0)
            return new { success = true, instructions, availableFiles = files };

        return new { success = true, instructions };
    }

    // ── Fork 执行（context:fork → 子 Agent）────────────────────────────────

    private async Task<object> ForkInvokeAsync(
        string skillId,
        SkillManifest manifest,
        string? sessionId,
        string? arguments,
        CancellationToken ct)
    {
        if (_subAgentRunner is null)
        {
            _logger.LogWarning("技能 {SkillId} 设置了 context:fork，但 ISubAgentRunner 未注入，降级为 inline 模式", skillId);
            return InlineInvoke(skillId, manifest, sessionId, arguments);
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning("技能 {SkillId} 设置了 context:fork，但当前无 sessionId，降级为 inline 模式", skillId);
            return InlineInvoke(skillId, manifest, sessionId, arguments);
        }

        // 渲染技能任务内容（复用 BuildSkillInstructionsFromManifest 统一处理 !command 注入 + $-替换）
        string task = _factory.BuildSkillInstructionsFromManifest(skillId, manifest, sessionId, arguments);

        // 解析 agent 类型：manifest.Agent → AgentStore.GetByName → fallback GetDefault
        string? agentId = ResolveAgentId(manifest.Agent, skillId);
        if (agentId is null)
        {
            _logger.LogWarning("技能 {SkillId} context:fork 无法解析 Agent，降级为 inline 模式", skillId);
            return InlineInvoke(skillId, manifest, sessionId, arguments);
        }

        // AllowedTools 约束通过 system prompt 注入子 Agent
        if (!string.IsNullOrWhiteSpace(manifest.AllowedTools))
            task = $"[TOOL RESTRICTION] You may only use the following tools: {manifest.AllowedTools}\n\n{task}";

        try
        {
            _logger.LogInformation("技能 {SkillId} 以 fork 模式在子 Agent {AgentId} 中执行", skillId, agentId);
            string result = await _subAgentRunner.RunSubAgentAsync(agentId, task, sessionId, ct);
            return new { success = true, fork = true, result };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "技能 {SkillId} fork 执行失败，降级为 inline 模式", skillId);
            return InlineInvoke(skillId, manifest, sessionId, arguments);
        }
    }

    /// <summary>
    /// 解析 fork 执行的目标 agentId：
    /// 1. manifest.Agent 非空 → 通过 IAgentLookup.GetByName 查找
    /// 2. 找不到 → fallback 到 default agent
    /// 3. IAgentLookup 未注入 → 返回 null
    /// </summary>
    private string? ResolveAgentId(string? agentType, string skillId)
    {
        if (_agentLookup is null) return null;

        if (!string.IsNullOrWhiteSpace(agentType))
        {
            string? id = _agentLookup.GetIdByName(agentType);
            if (id is not null) return id;

            _logger.LogWarning("技能 {SkillId} 指定 agent 类型 '{AgentType}' 未找到，回退到 default agent", skillId, agentType);
        }

        return _agentLookup.GetDefaultId();
    }

    // ── Hooks 执行 ─────────────────────────────────────────────────────────

    private async Task<HookExecutionResult> ExecuteHooksAsync(
        IReadOnlyList<SkillHookEntry> hooks,
        string skillId,
        string hookType)
    {
        if (hooks.Count == 0) return HookExecutionResult.Ok;
        if (!_options.AllowCommandInjection)
        {
            _logger.LogDebug("Skill {SkillId} {HookType} hooks 已跳过（AllowCommandInjection=false）", skillId, hookType);
            return HookExecutionResult.Ok;
        }

        string skillDir = _skillService.GetSkillDirectory(skillId);
        foreach (SkillHookEntry hook in hooks)
        {
            if (!string.Equals(hook.Type, "command", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skill {SkillId} {HookType} hook 类型 '{Type}' 暂不支持，跳过", skillId, hookType, hook.Type);
                continue;
            }

            try
            {
                CommandResult cmdResult = await Task.Run(() =>
                    _skillService.ExecuteCommand(hook.Command, skillDir, hook.TimeoutSeconds));

                if (cmdResult.ExitCode != 0)
                {
                    string errorDetail = $"exit code {cmdResult.ExitCode}: {cmdResult.Stderr}".Trim();
                    if (hook.FailOnError)
                    {
                        _logger.LogWarning("Skill {SkillId} {HookType} hook 失败且 FailOnError=true: {Command} → {Error}",
                            skillId, hookType, hook.Command, errorDetail);
                        return new HookExecutionResult(false, $"on-invoke hook failed: {hook.Command} ({errorDetail})");
                    }
                    _logger.LogWarning("Skill {SkillId} {HookType} hook 返回非零: {Command} → {Error}",
                        skillId, hookType, hook.Command, errorDetail);
                }
                else
                {
                    _logger.LogDebug("Skill {SkillId} {HookType} hook 执行完成: {Command}", skillId, hookType, hook.Command);
                }
            }
            catch (Exception ex)
            {
                if (hook.FailOnError)
                {
                    _logger.LogWarning(ex, "Skill {SkillId} {HookType} hook 异常且 FailOnError=true: {Command}", skillId, hookType, hook.Command);
                    return new HookExecutionResult(false, $"on-invoke hook exception: {hook.Command} ({ex.Message})");
                }
                _logger.LogWarning(ex, "Skill {SkillId} {HookType} hook 执行失败: {Command}", skillId, hookType, hook.Command);
            }
        }

        return HookExecutionResult.Ok;
    }
}

/// <summary>Hook 执行结果。</summary>
internal sealed record HookExecutionResult(bool Success, string? Error = null)
{
    public static readonly HookExecutionResult Ok = new(true);
}


