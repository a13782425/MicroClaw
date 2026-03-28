using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// 工具收集器 — 统一工具收集、过滤、MCP 连接管理逻辑。
/// 替代 AgentRunner 中散落的 CollectBuiltinTools / FilterMcpTools / GetEnabledMcpServers 和
/// ToolsEndpoints / AgentEndpoints 中的重复遍历逻辑。
/// </summary>
public sealed class ToolCollector(
    IEnumerable<IToolProvider> providers,
    McpServerConfigStore mcpServerConfigStore,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<ToolCollector>();

    /// <summary>
    /// 按 Agent 配置和运行时上下文收集所有可用工具（含 MCP 连接），返回可释放的结果。
    /// 调用方使用 <c>await using</c> 确保 MCP 连接释放。
    /// </summary>
    public async Task<ToolCollectionResult> CollectToolsAsync(
        AgentConfig agent, ToolCreationContext context, CancellationToken ct = default)
    {
        var result = new ToolCollectionResult();

        // ── 1. DI 注册的 IToolProvider（builtin + channel + skill）──────────
        foreach (IToolProvider provider in providers)
        {
            // 按 ToolGroupConfig 整体启用/禁用
            ToolGroupConfig? cfg = agent.ToolGroupConfigs
                .FirstOrDefault(g => g.GroupId == provider.GroupId);
            if (cfg is not null && !cfg.IsEnabled) continue;

            try
            {
                ToolProviderResult providerResult = await provider.CreateToolsAsync(context, ct);
                if (providerResult.Tools.Count == 0) continue;

                // 按 DisabledToolNames 过滤单个工具
                IEnumerable<AITool> filtered = cfg is null
                    ? providerResult.Tools
                    : providerResult.Tools.Where(t => !cfg.DisabledToolNames.Contains(t.Name));

                result.AddTools(filtered);

                if (providerResult.Disposables is { Count: > 0 })
                    result.TrackDisposables(providerResult.Disposables);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "工具提供者 {GroupId} 创建工具失败，跳过", provider.GroupId);
            }
        }

        // ── 2. 动态 MCP 工具（按 Agent 引用和 ToolGroupConfig 过滤）────────────
        IReadOnlyList<McpServerConfig> enabledServers = GetEnabledMcpServers(agent);
        if (enabledServers.Count > 0)
        {
            foreach (McpServerConfig srv in enabledServers)
            {
                try
                {
                    var mcpProvider = new McpToolProvider(srv, loggerFactory);
                    ToolProviderResult mcpResult = await mcpProvider.CreateToolsAsync(context, ct);

                    // 按 ToolGroupConfig 过滤单个 MCP 工具
                    ToolGroupConfig? srvCfg = agent.ToolGroupConfigs
                        .FirstOrDefault(g => g.GroupId == srv.Name || g.GroupId == srv.Id);

                    IEnumerable<AITool> filtered = srvCfg is null
                        ? mcpResult.Tools
                        : mcpResult.Tools.Where(t => !srvCfg.DisabledToolNames.Contains(t.Name));

                    result.AddTools(filtered);

                    if (mcpResult.Disposables is { Count: > 0 })
                        result.TrackDisposables(mcpResult.Disposables);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MCP Server {McpServerName} 工具加载失败，跳过", srv.Name);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 返回所有工具分组信息（含启用状态），供 API 端点展示。
    /// <paramref name="agent"/> 为 null 时返回全局视图（不做 Agent 级过滤）。
    /// </summary>
    public async Task<IReadOnlyList<ToolGroupInfo>> GetToolGroupsAsync(
        AgentConfig? agent, CancellationToken ct = default)
    {
        var groups = new List<ToolGroupInfo>();

        // ── DI 注册的 IToolProvider（builtin + channel + skill）──────────────
        foreach (IToolProvider provider in providers)
        {
            ToolGroupConfig? cfg = agent?.ToolGroupConfigs
                .FirstOrDefault(g => g.GroupId == provider.GroupId);
            bool groupEnabled = cfg is null || cfg.IsEnabled;

            groups.Add(new ToolGroupInfo(
                Id: provider.GroupId,
                Name: provider.DisplayName,
                Type: provider.Category == ToolCategory.Mcp ? "mcp" : provider.Category == ToolCategory.Channel ? "channel" : "builtin",
                IsEnabled: groupEnabled,
                Tools: provider.GetToolDescriptions().Select(t => new ToolInfo(
                    Name: t.Name,
                    Description: t.Description,
                    IsEnabled: groupEnabled && (cfg is null || !cfg.DisabledToolNames.Contains(t.Name))
                )).ToList()));
        }

        // ── MCP Server 分组 ─────────────────────────────────────────────────
        IReadOnlyList<McpServerConfig> allServers = mcpServerConfigStore.All;
        HashSet<string>? disabledIds = agent is not null && agent.DisabledMcpServerIds.Count > 0
            ? agent.DisabledMcpServerIds.ToHashSet()
            : null;

        foreach (McpServerConfig srv in allServers)
        {
            bool isEnabled = srv.IsEnabled && (disabledIds is null || !disabledIds.Contains(srv.Id));

            // 按 ToolGroupConfig 的整体启用/禁用
            ToolGroupConfig? srvCfg = agent?.ToolGroupConfigs
                .FirstOrDefault(g => g.GroupId == srv.Name || g.GroupId == srv.Id);
            bool groupEnabled = isEnabled && (srvCfg is null || srvCfg.IsEnabled);

            if (!groupEnabled)
            {
                groups.Add(new ToolGroupInfo(
                    Id: srv.Id, Name: srv.Name, Type: "mcp",
                    IsEnabled: false, Tools: []));
                continue;
            }

            // 连接 MCP Server 获取工具描述
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var mcpProvider = new McpToolProvider(srv, loggerFactory);
                ToolProviderResult mcpResult = await mcpProvider.CreateToolsAsync(
                    new ToolCreationContext(), cts.Token);

                try
                {
                    groups.Add(new ToolGroupInfo(
                        Id: srv.Id, Name: srv.Name, Type: "mcp",
                        IsEnabled: true,
                        Tools: mcpResult.Tools.Select(t => new ToolInfo(
                            Name: t.Name,
                            Description: t.Description ?? string.Empty,
                            IsEnabled: srvCfg is null || !srvCfg.DisabledToolNames.Contains(t.Name)
                        )).ToList()));
                }
                finally
                {
                    if (mcpResult.Disposables is not null)
                        foreach (IAsyncDisposable conn in mcpResult.Disposables)
                            try { await conn.DisposeAsync(); } catch { }
                }
            }
            catch
            {
                groups.Add(new ToolGroupInfo(
                    Id: srv.Id, Name: srv.Name, Type: "mcp",
                    IsEnabled: true, Tools: [], LoadError: true));
            }
        }

        return groups;
    }

    /// <summary>返回未被整体禁用的 MCP Server 配置列表（排除 Agent 级别禁用项）。</summary>
    private IReadOnlyList<McpServerConfig> GetEnabledMcpServers(AgentConfig agent)
    {
        IReadOnlyList<McpServerConfig> servers = mcpServerConfigStore.AllEnabled;
        // 排除 Agent 级别禁用的 MCP Server
        if (agent.DisabledMcpServerIds.Count > 0)
        {
            HashSet<string> disabled = agent.DisabledMcpServerIds.ToHashSet();
            servers = servers.Where(s => !disabled.Contains(s.Id)).ToList().AsReadOnly();
        }
        if (agent.ToolGroupConfigs.Count == 0) return servers;
        return servers
            .Where(s =>
            {
                ToolGroupConfig? cfg = agent.ToolGroupConfigs
                    .FirstOrDefault(g => g.GroupId == s.Name || g.GroupId == s.Id);
                return cfg is null || cfg.IsEnabled;
            })
            .ToList()
            .AsReadOnly();
    }
}

/// <summary>工具分组信息（供 API 端点返回）。</summary>
public sealed record ToolGroupInfo(
    string Id,
    string Name,
    string Type,
    bool IsEnabled,
    IReadOnlyList<ToolInfo> Tools,
    bool LoadError = false);

/// <summary>单个工具信息（供 API 端点返回）。</summary>
public sealed record ToolInfo(
    string Name,
    string Description,
    bool IsEnabled);
