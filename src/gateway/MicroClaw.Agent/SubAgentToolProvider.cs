using System.ComponentModel;
using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>
/// 子代理工具提供者。
/// 为每个已启用的非默认 Agent 动态生成一个具名 <see cref="AIFunction"/>（名称由 Agent 显示名称规范化而来），
/// 并附加 <c>write_agent_memory</c> 工具。
/// </summary>
public sealed class SubAgentToolProvider(
    AgentStore agentStore,
    ISubAgentRunner subAgentRunner,
    AgentDnaService agentDnaService,
    ISessionReader sessionReader) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "subagent";
    public string DisplayName => "子代理 & DNA";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        SubAgentTools.GetToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return Task.FromResult(ToolProviderResult.Empty);

        var tools = new List<AIFunction>();

        // 为每个已启用的非默认子代理创建专属具名工具
        foreach (AgentConfig subAgent in agentStore.All.Where(a => !a.IsDefault && a.IsEnabled))
        {
            string toolName = SubAgentTools.SanitizeAgentName(subAgent.Name);
            string agentId = subAgent.Id;
            string agentName = subAgent.Name;
            string description = string.IsNullOrWhiteSpace(subAgent.Description)
                ? $"调用子代理「{agentName}」执行专项任务并等待结果。"
                : $"子代理「{agentName}」：{subAgent.Description.TrimEnd('。', '.')}。调用时传入详细的任务描述。";

            string sessionId = context.SessionId;
            tools.Add(AIFunctionFactory.Create(
                async ([Description("交给子代理详细执行的任务描述，尽量具体，子代理会基于此独立完成任务")] string task,
                       CancellationToken ct) =>
                {
                    try
                    {
                        string result = await subAgentRunner.RunSubAgentAsync(agentId, task, sessionId, ct);
                        return (object)new { success = true, agentId, agentName, result };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: toolName,
                description: description));
        }

        // 固定保留 write_agent_memory 工具
        tools.Add(SubAgentTools.CreateWriteMemoryTool(context.SessionId, agentStore, agentDnaService, sessionReader));

        return Task.FromResult(new ToolProviderResult(tools));
    }
}
