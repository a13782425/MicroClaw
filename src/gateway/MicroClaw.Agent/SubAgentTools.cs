using System.ComponentModel;
using System.Text.RegularExpressions;
using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>
/// 内置 Agent DNA 工具：持久化记忆写入。
/// 子代理调用已改为每个 Agent 一个具名工具，由 <see cref="SubAgentToolProvider"/> 动态生成。
/// </summary>
public static class SubAgentTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("write_agent_memory", "将重要信息追加写入当前 Agent 的长期记忆（MEMORY.md）。该记忆在所有会话中共享，下次对话时自动注入 SystemPrompt。适合存储用户偏好、关键约定、阶段性结论等跨会话经验。"),
    ];

    /// <summary>返回内置工具元数据（供工具列表 API 使用，不需要 sessionId）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 将 Agent 显示名称规范化为合法的 AIFunction 名称（仅保留字母、数字、下划线，长度限 64）。
    /// </summary>
    public static string SanitizeAgentName(string name)
    {
        string safe = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_");
        safe = safe.Trim('_');
        if (safe.Length == 0) safe = "agent";
        return safe.Length > 64 ? safe[..64] : safe;
    }

    /// <summary>创建 write_agent_memory 工具（可复用至多处）。</summary>
    public static AIFunction CreateWriteMemoryTool(
        string sessionId,
        AgentStore agentStore,
        AgentDnaService agentDnaService,
        ISessionReader sessionReader)
    {
        return AIFunctionFactory.Create(
                (
                    [Description("要追加写入 Agent 长期记忆的 Markdown 格式内容（如用户偏好、关键约定、阶段性结论）。")] string content) =>
                {
                    if (string.IsNullOrWhiteSpace(content))
                        return (object)new { success = false, error = "content 不能为空。" };

                    try
                    {
                        // 从当前会话获取关联的 AgentId
                        SessionInfo? session = sessionReader.Get(sessionId);
                        string? agentId = session?.AgentId;

                        // 若会话未绑定 Agent，回退到默认 Agent
                        if (string.IsNullOrWhiteSpace(agentId))
                        {
                            AgentConfig? defaultAgent = agentStore.GetDefault();
                            agentId = defaultAgent?.Id;
                        }

                        if (string.IsNullOrWhiteSpace(agentId))
                            return (object)new { success = false, error = "无法确定当前 Agent，无法写入记忆。" };

                        agentDnaService.AppendMemory(agentId, content);
                        string currentMemory = agentDnaService.GetMemory(agentId);
                        return (object)new { success = true, agentId, charCount = currentMemory.Length };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "write_agent_memory",
                description: "将重要信息追加写入当前 Agent 的长期记忆（MEMORY.md）。该记忆在所有会话中共享，下次对话时自动注入 SystemPrompt。适合存储用户偏好、关键约定、阶段性结论等跨会话经验。");
    }
}
