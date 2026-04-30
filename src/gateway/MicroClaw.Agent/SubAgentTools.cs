using System.ComponentModel;
using System.Text.RegularExpressions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>
/// 内置子代理工具工厂：提供 Agent 管理工具集（列举/查询/创建/删除/修改代理）。
/// 由 <see cref="SubAgentToolProvider"/> 动态注入到每次会话。
/// </summary>
public static class SubAgentTools
{
    internal static IReadOnlySet<string> ManagementToolNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "list_agents",
        "get_agent",
        "create_agent",
        "delete_agent",
        "update_agent_info",
        "enable_disable_agent",
        "update_agent_sub_agents"
    };

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

    internal static string BuildUniqueToolName(string name, ISet<string> usedNames)
    {
        ArgumentNullException.ThrowIfNull(usedNames);

        string baseName = SanitizeAgentName(name);
        string candidate = baseName;
        int suffix = 2;

        while (!usedNames.Add(candidate))
        {
            string suffixText = $"_{suffix++}";
            int maxBaseLength = Math.Max(1, 64 - suffixText.Length);
            string trimmedBaseName = baseName.Length > maxBaseLength ? baseName[..maxBaseLength] : baseName;
            candidate = trimmedBaseName + suffixText;
        }

        return candidate;
    }

    /// <summary>
    /// 创建 Agent 管理工具集（7 个），供 <see cref="SubAgentToolProvider"/> 注入。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateAgentManagementTools(
        IMicroAgentService agentService,
        MicroAgentService microAgentService,
        AgentDnaService agentDnaService)
    {
        return
        [
            // 1. list_agents
            AIFunctionFactory.Create(
                () =>
                {
                    var agents = agentService.All.Select(a => new
                    {
                        a.Id,
                        a.Name,
                        a.Description,
                        a.IsEnabled,
                        a.IsDefault,
                        allowedSubAgentIds = a.AllowedSubAgentIds,
                    });
                    return (object)new { success = true, agents };
                },
                name: "list_agents",
                description: "列举系统中所有 Agent 代理及其基本信息（id、名称、描述、启用状态、是否默认代理、子代理白名单）。"),

            // 2. get_agent
            AIFunctionFactory.Create(
                ([Description("要查询的 Agent ID")] string agentId) =>
                {
                    IMicroAgent? agent = agentService.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"Agent '{agentId}' 不存在。" };

                    return (object)new
                    {
                        success = true,
                        agent = new
                        {
                            agent.Id,
                            agent.Name,
                            agent.Description,
                            agent.IsEnabled,
                            agent.IsDefault,
                            allowedSubAgentIds = agent.AllowedSubAgentIds,
                            systemPrompt = agentDnaService.GetSoul(agentId),
                        },
                    };
                },
                name: "get_agent",
                description: "查询单个 Agent 代理的完整详情，包含名称、描述、系统提示词（Soul）、子代理白名单等信息。"),

            // 3. create_agent
            AIFunctionFactory.Create(
                async ([Description("新代理名称（唯一，不可与已有代理重名）")] string name,
                       [Description("代理的功能描述，帮助其他代理了解其用途（可为空）")] string? description,
                       [Description("代理的系统提示词（Soul），定义代理的人格、语气和专长范围（可为空，为空时使用默认模板）")] string? systemPrompt,
                       CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                        return (object)new { success = false, error = "name 不能为空。" };

                    try
                    {
                        MicroAgent config = MicroAgent.New(
                            sp: microAgentService.ServiceProvider,
                            name: name.Trim(),
                            description: description?.Trim() ?? string.Empty,
                            isEnabled: true,
                            contextWindowMessages: 20);

                        MicroAgent created = await microAgentService.CreateAgentAsync(config, ct);
                        try
                        {
                            agentDnaService.InitializeAgent(created.Id);

                            if (!string.IsNullOrWhiteSpace(systemPrompt))
                                agentDnaService.UpdateSoul(created.Id, systemPrompt.Trim());
                        }
                        catch
                        {
                            try
                            {
                                await microAgentService.DeleteAgentAsync(created.Id, CancellationToken.None);
                            }
                            catch
                            {
                            }

                            try
                            {
                                agentDnaService.DeleteAgentFiles(created.Id);
                            }
                            catch
                            {
                            }

                            throw;
                        }

                        return (object)new { success = true, agentId = created.Id, name = created.Name };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "create_agent",
                description: "创建一个新的 Agent 代理。需指定唯一名称；可选填功能描述和系统提示词（Soul）；默认上下文窗口 20 条，创建后立即可用。"),

            // 4. delete_agent
            AIFunctionFactory.Create(
                async ([Description("要删除的 Agent ID")] string agentId, CancellationToken ct) =>
                {
                    IMicroAgent? agent = agentService.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"Agent '{agentId}' 不存在。" };
                    if (agent.IsDefault)
                        return (object)new { success = false, error = "默认代理不可删除，请通过管理界面操作。" };

                    try
                    {
                        await microAgentService.DeleteAgentAsync(agentId, ct);

                        try
                        {
                            agentDnaService.DeleteAgentFiles(agentId);
                        }
                        catch
                        {
                        }

                        return (object)new { success = true, agentId };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "delete_agent",
                description: "【不可逆操作，执行前请向用户确认】永久删除一个 Agent 代理及其 DNA 文件。默认代理不可删除。"),

            // 5. update_agent_info
            AIFunctionFactory.Create(
                async ([Description("要修改的 Agent ID")] string agentId,
                       [Description("新名称（null 或空字符串表示不修改）")] string? name,
                       [Description("新的功能描述（null 表示不修改）")] string? description,
                       CancellationToken ct) =>
                {
                    IMicroAgent? agent = agentService.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"Agent '{agentId}' 不存在。" };
                    if (agent.IsDefault)
                        return (object)new { success = false, error = "默认代理的信息不可修改，请通过管理界面操作。" };

                    try
                    {
                        MicroAgent result = await microAgentService.UpdateAgentAsync(agentId, existing =>
                            existing.UpdateInfo(
                                string.IsNullOrWhiteSpace(name) ? existing.Name : name.Trim(),
                                description is null ? existing.Description : description.Trim()), ct);
                        return (object)new { success = true, agentId, name = result.Name, description = result.Description };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "update_agent_info",
                description: "【默认代理不可修改】修改 Agent 代理的名称或功能描述。name 和 description 均可选，只传需要修改的字段。"),

            // 6. enable_disable_agent
            AIFunctionFactory.Create(
                async ([Description("要修改的 Agent ID")] string agentId,
                       [Description("true = 启用，false = 禁用")] bool isEnabled,
                       CancellationToken ct) =>
                {
                    IMicroAgent? agent = agentService.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"Agent '{agentId}' 不存在。" };
                    if (agent.IsDefault)
                        return (object)new { success = false, error = "默认代理不可禁用，请通过管理界面操作。" };

                    try
                    {
                        MicroAgent result = await microAgentService.UpdateAgentAsync(agentId, existing =>
                        {
                            existing.IsEnabled = isEnabled;
                        }, ct);
                        return (object)new { success = true, agentId, isEnabled = result.IsEnabled };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "enable_disable_agent",
                description: "【不可逆操作，执行前请向用户确认】启用或禁用一个 Agent 代理。被禁用的代理将无法被调用为子代理。默认代理不可禁用。"),

            // 7. update_agent_sub_agents
            AIFunctionFactory.Create(
                async ([Description("要修改的 Agent ID")] string agentId,
                       [Description("子代理白名单：null = 允许调用所有代理；空数组 = 禁止调用任何子代理；字符串数组 = 仅允许指定 ID 的子代理")] IReadOnlyList<string>? allowedSubAgentIds,
                       CancellationToken ct) =>
                {
                    IMicroAgent? agent = agentService.GetById(agentId);
                    if (agent is null)
                        return (object)new { success = false, error = $"Agent '{agentId}' 不存在。" };
                    if (agent.IsDefault)
                        return (object)new { success = false, error = "默认代理的子代理白名单不可修改，请通过管理界面操作。" };

                    try
                    {
                        MicroAgent result = await microAgentService.UpdateAgentAsync(agentId, existing => existing.UpdateAllowedSubAgentIds(allowedSubAgentIds), ct);
                        return (object)new { success = true, agentId, allowedSubAgentIds = result.AllowedSubAgentIds };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "update_agent_sub_agents",
                description: "修改 Agent 代理可调用的子代理白名单。null = 全部可调用；空数组 = 禁止调用任何子代理；指定 ID 数组 = 仅允许白名单内的代理。默认代理不可修改。"),
        ];
    }
}
