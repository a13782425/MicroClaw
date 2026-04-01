using System.ComponentModel;
using MicroClaw.Agent.Memory;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>
/// 子代理工具提供者。
/// 根据 <see cref="ToolCreationContext"/> 中的调用代理 ID、祖先链和 ACL 白名单，
/// 为当前代理动态生成可调用的子代理工具函数，并附加 Agent 管理工具集。
/// </summary>
public sealed class SubAgentToolProvider(
    AgentStore agentStore,
    ISubAgentRunner subAgentRunner,
    AgentDnaService agentDnaService) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Core;
    public string GroupId => "subagent";
    public string DisplayName => "子代理 & Agent 管理";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() => [];

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return Task.FromResult(ToolProviderResult.Empty);

        var tools = new List<AIFunction>();

        // 构建排除集合：调用者自身 + 祖先链中的所有代理
        var excludedIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.CallingAgentId))
            excludedIds.Add(context.CallingAgentId);
        if (context.AncestorAgentIds is { Count: > 0 })
            foreach (string id in context.AncestorAgentIds)
                excludedIds.Add(id);

        // ACL 白名单：null = 全部可调用，非 null = 仅允许指定 ID
        HashSet<string>? allowedIds = context.AllowedSubAgentIds is not null
            ? new HashSet<string>(context.AllowedSubAgentIds, StringComparer.Ordinal)
            : null;

        foreach (AgentConfig subAgent in agentStore.All.Where(a => a.IsEnabled))
        {
            // 排除自身和祖先链
            if (excludedIds.Contains(subAgent.Id)) continue;

            // ACL 白名单过滤
            if (allowedIds is not null && !allowedIds.Contains(subAgent.Id)) continue;
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

        // 固定追加 Agent 管理工具集
        tools.AddRange(SubAgentTools.CreateAgentManagementTools(agentStore, agentDnaService));

        return Task.FromResult(new ToolProviderResult(tools));
    }
}
