using System.ComponentModel;
using System.Text;
using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>
/// 子代理启动内置工具：让 LLM 主动调用子代理完成专项任务（类似 Claude Code 的子任务机制）。
/// 子会话会被持久化，可在会话列表中查看执行历史。
/// </summary>
public static class SubAgentTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("spawn_subagent", "启动子代理会话执行专项任务，并等待结果。当主任务需要将部分工作委派给具有特定专长的子代理时使用。子代理会话会被持久化，可在会话列表中查看执行历史。"),
        ("write_session_dna", "将重要信息持久化写入当前会话的 DNA 记忆（会话级私有上下文）。下次对话时该信息会自动注入 SystemPrompt，无需用户重复说明。适合存储用户偏好、关键约定、阶段性结论等。"),
    ];

    /// <summary>返回内置工具元数据（供工具列表 API 使用，不需要 sessionId）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 为指定 Session 创建内置工具列表（子代理 + 会话 DNA 写入）。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateForSession(
        string sessionId,
        AgentStore agentStore,
        ISubAgentRunner subAgentRunner,
        DNAService dnaService)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("子代理的 ID（从可用的 Agent 列表获取）")] string agentId,
                    [Description("交给子代理详细执行的任务描述，尽量具体，子代理会基于此独立完成任务")] string task,
                    CancellationToken ct) =>
                {
                    AgentConfig? agent = agentStore.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"子代理 '{agentId}' 不存在。" };
                    if (!agent.IsEnabled)
                        return (object)new { success = false, error = $"子代理 '{agent.Name}' 未启用。" };
                    if (agent.IsDefault)
                        return (object)new { success = false, error = "不能将主代理作为子代理调用，请选择其他代理。" };

                    try
                    {
                        string result = await subAgentRunner.RunSubAgentAsync(agentId, task, sessionId, ct);
                        return (object)new { success = true, agentId, agentName = agent.Name, result };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "spawn_subagent",
                description: "启动子代理会话执行专项任务，并等待结果。当主任务需要将部分工作委派给具有特定专长的子代理时使用。子代理会话会被持久化，可在会话列表中查看执行历史。"),

            AIFunctionFactory.Create(
                (
                    [Description("记忆文件名，建议使用英文短名（如 user-prefs.md）。若不含 .md 后缀会自动补全。")] string fileName,
                    [Description("要写入的 Markdown 格式内容，完整覆盖写入（非追加）。")] string content,
                    [Description("可选子目录分类标签（如 preferences、decisions），用于归类管理。留空则写到根目录。")] string? category) =>
                {
                    // 路径净化：防止路径穿越
                    string safeName = Path.GetFileName(fileName.Trim());
                    if (string.IsNullOrWhiteSpace(safeName))
                        return (object)new { success = false, error = "fileName 不能为空。" };

                    // 自动补全 .md 后缀
                    if (!safeName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        safeName += ".md";

                    string safeCategory = SanitizeCategory(category);

                    try
                    {
                        GeneFile written = dnaService.WriteSession(sessionId, safeCategory, safeName, content ?? string.Empty);
                        int sizeBytes = Encoding.UTF8.GetByteCount(written.Content);
                        return (object)new { success = true, fileName = written.FileName, category = written.Category, sizeBytes };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "write_session_dna",
                description: "将重要信息持久化写入当前会话的 DNA 记忆（会话级私有上下文）。下次对话时该信息会自动注入 SystemPrompt，无需用户重复说明。适合存储用户偏好、关键约定、阶段性结论等。"),
        ];
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
