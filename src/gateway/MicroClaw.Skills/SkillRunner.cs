using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Skills;

/// <summary>
/// Skill 脚本执行引擎。
/// 根据 SkillType 启动子进程（python/node/bash），通过 stdin 传递参数 JSON，
/// 捕获 stdout 作为返回値，注入环境变量并应用可配置超时（默认 30 秒）。
/// </summary>
public sealed class SkillRunner(SkillService skillService, ILogger<SkillRunner> logger)
{

    /// <summary>
    /// 执行技能脚本并返回输出内容。
    /// </summary>
    /// <param name="skill">技能配置</param>
    /// <param name="argsJson">参数 JSON 字符串（将通过 stdin 传给脚本）</param>
    /// <param name="workspaceRoot">workspace 根目录（注入给脚本）</param>
    /// <param name="sessionId">当前会话 ID（注入给脚本）</param>
    /// <param name="ct">取消令牌</param>
    public async Task<string> ExecuteAsync(
        SkillConfig skill,
        string argsJson,
        string workspaceRoot,
        string sessionId,
        CancellationToken ct = default)
    {
        string skillDir = Path.Combine(workspaceRoot, "skills", skill.Id);
        var (command, args) = ResolveCommand(skill.SkillType, skill.EntryPoint);

        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = skillDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // 注入环境变量供脚本使用
        psi.Environment["MICROCLAW_SESSION_ID"] = sessionId;
        psi.Environment["MICROCLAW_SKILL_DIR"] = skillDir;
        psi.Environment["MICROCLAW_WORKSPACE_DIR"] = workspaceRoot;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(skill.TimeoutSeconds));

        using Process process = new() { StartInfo = psi };
        process.Start();

        // 通过 stdin 传递参数 JSON
        await process.StandardInput.WriteLineAsync(argsJson);
        process.StandardInput.Close();

        string stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        string stderr = await process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            logger.LogWarning("Skill {SkillId} execution timed out after {Timeout}s", skill.Id, skill.TimeoutSeconds);
            throw new TimeoutException($"Skill '{skill.Name}' execution timed out after {skill.TimeoutSeconds} seconds.");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogWarning("Skill {SkillId} stderr: {Stderr}", skill.Id, stderr);

        if (process.ExitCode != 0)
            logger.LogWarning("Skill {SkillId} exited with code {ExitCode}", skill.Id, process.ExitCode);

        return stdout.Trim();
    }

    private static (string command, string args) ResolveCommand(string skillType, string entryPoint)
    {
        return skillType.ToLowerInvariant() switch
        {
            "python" => ("python", entryPoint),
            "nodejs" or "node" => ("node", entryPoint),
            "shell" or "bash" or "sh" => ("bash", entryPoint),
            _ => throw new NotSupportedException($"Skill type '{skillType}' is not supported. Supported types: python, nodejs, shell."),
        };
    }
}
