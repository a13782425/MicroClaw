using System.ComponentModel;
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
    ];

    /// <summary>返回 spawn_subagent 工具元数据（供工具列表 API 使用，不需要 sessionId）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 为指定 Session 创建子代理工具列表。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateForSession(
        string sessionId,
        AgentStore agentStore,
        ISubAgentRunner subAgentRunner)
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
                description: "启动子代理会话执行专项任务，并等待结果。当主任务需要将部分工作委派给具有特定专长的子代理时使用。子代理会话会被持久化，可在会话列表中查看执行历史。")
        ];
    }
}
