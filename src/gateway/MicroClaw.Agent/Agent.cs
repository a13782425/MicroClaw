using MicroClaw.Providers;
using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 实体（领域对象）：聚合工具权限、子代理策略、路由配置等行为。
/// 使用 <see cref="Create"/> 创建新 Agent，<see cref="Reconstitute"/> 从持久化状态恢复。
/// </summary>
public sealed class Agent
{
    private List<string> _disabledSkillIds = [];
    private List<string> _disabledMcpServerIds = [];
    private List<ToolGroupConfig> _toolGroupConfigs = [];
    private List<string>? _allowedSubAgentIds;

    private Agent() { }  // 强制通过工厂方法创建

    // ── 属性 ─────────────────────────────────────────────────────────────

    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int? ContextWindowMessages { get; private set; }
    public bool ExposeAsA2A { get; private set; }
    public ProviderRoutingStrategy RoutingStrategy { get; private set; }
    public decimal? MonthlyBudgetUsd { get; private set; }

    public IReadOnlyList<string> DisabledSkillIds => _disabledSkillIds.AsReadOnly();
    public IReadOnlyList<string> DisabledMcpServerIds => _disabledMcpServerIds.AsReadOnly();
    public IReadOnlyList<ToolGroupConfig> ToolGroupConfigs => _toolGroupConfigs.AsReadOnly();
    public IReadOnlyList<string>? AllowedSubAgentIds => _allowedSubAgentIds?.AsReadOnly();

    // ── 工厂方法 ──────────────────────────────────────────────────────────

    /// <summary>创建新 Agent（Id 为空，由 IAgentRepository.Save 分配）。</summary>
    public static Agent Create(
        string name,
        string description,
        bool isEnabled,
        IReadOnlyList<string>? disabledSkillIds = null,
        IReadOnlyList<string>? disabledMcpServerIds = null,
        IReadOnlyList<ToolGroupConfig>? toolGroupConfigs = null,
        bool isDefault = false,
        int? contextWindowMessages = null,
        bool exposeAsA2A = false,
        IReadOnlyList<string>? allowedSubAgentIds = null,
        ProviderRoutingStrategy routingStrategy = ProviderRoutingStrategy.Default,
        decimal? monthlyBudgetUsd = null) =>
        new()
        {
            Id = string.Empty,
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            IsDefault = isDefault,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ContextWindowMessages = contextWindowMessages,
            ExposeAsA2A = exposeAsA2A,
            RoutingStrategy = routingStrategy,
            MonthlyBudgetUsd = monthlyBudgetUsd,
            _disabledSkillIds = [.. (disabledSkillIds ?? [])],
            _disabledMcpServerIds = [.. (disabledMcpServerIds ?? [])],
            _toolGroupConfigs = [.. (toolGroupConfigs ?? [])],
            _allowedSubAgentIds = allowedSubAgentIds is null ? null : [.. allowedSubAgentIds],
        };

    /// <summary>从持久化状态恢复 Agent 实例。</summary>
    public static Agent Reconstitute(
        string id,
        string name,
        string description,
        bool isEnabled,
        IReadOnlyList<string> disabledSkillIds,
        IReadOnlyList<string> disabledMcpServerIds,
        IReadOnlyList<ToolGroupConfig> toolGroupConfigs,
        DateTimeOffset createdAtUtc,
        bool isDefault = false,
        int? contextWindowMessages = null,
        bool exposeAsA2A = false,
        IReadOnlyList<string>? allowedSubAgentIds = null,
        ProviderRoutingStrategy routingStrategy = ProviderRoutingStrategy.Default,
        decimal? monthlyBudgetUsd = null) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            IsDefault = isDefault,
            CreatedAtUtc = createdAtUtc,
            ContextWindowMessages = contextWindowMessages,
            ExposeAsA2A = exposeAsA2A,
            RoutingStrategy = routingStrategy,
            MonthlyBudgetUsd = monthlyBudgetUsd,
            _disabledSkillIds = [.. disabledSkillIds],
            _disabledMcpServerIds = [.. disabledMcpServerIds],
            _toolGroupConfigs = [.. toolGroupConfigs],
            _allowedSubAgentIds = allowedSubAgentIds is null ? null : [.. allowedSubAgentIds],
        };

    // ── 行为方法：生命周期 ────────────────────────────────────────────────

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    public void UpdateInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public void UpdateContextWindow(int? contextWindowMessages)
        => ContextWindowMessages = contextWindowMessages;

    public void UpdateExposeAsA2A(bool expose) => ExposeAsA2A = expose;

    public void UpdateRoutingStrategy(ProviderRoutingStrategy strategy) => RoutingStrategy = strategy;

    public void UpdateMonthlyBudget(decimal? budgetUsd) => MonthlyBudgetUsd = budgetUsd;

    // ── 行为方法：Tool 权限检查（O-2-2）──────────────────────────────────

    /// <summary>检查指定工具组是否整体启用（未配置时默认启用）。</summary>
    public bool IsToolGroupEnabled(string groupId)
    {
        ToolGroupConfig? cfg = _toolGroupConfigs.FirstOrDefault(g => g.GroupId == groupId);
        return cfg is null || cfg.IsEnabled;
    }

    /// <summary>检查指定工具组内的单个工具是否被禁用。</summary>
    public bool IsToolDisabled(string groupId, string toolName)
    {
        ToolGroupConfig? cfg = _toolGroupConfigs.FirstOrDefault(g => g.GroupId == groupId);
        return cfg is not null && cfg.DisabledToolNames.Contains(toolName);
    }

    /// <summary>更新工具分组启用配置。</summary>
    public void UpdateToolGroupConfigs(IReadOnlyList<ToolGroupConfig> configs)
        => _toolGroupConfigs = [.. configs];

    // ── 行为方法：MCP/Skill 禁用管理（O-2-3）────────────────────────────

    /// <summary>检查指定 MCP Server 是否被禁用（按 Id 或 Name 匹配）。</summary>
    public bool IsMcpServerDisabled(string serverIdOrName)
        => _disabledMcpServerIds.Contains(serverIdOrName);

    /// <summary>更新禁用的 MCP Server 列表。</summary>
    public void UpdateDisabledMcpServerIds(IReadOnlyList<string> ids)
        => _disabledMcpServerIds = [.. ids];

    /// <summary>检查指定 Skill 是否被禁用。</summary>
    public bool IsSkillDisabled(string skillId)
        => _disabledSkillIds.Contains(skillId);

    /// <summary>更新禁用的 Skill 列表。</summary>
    public void UpdateDisabledSkillIds(IReadOnlyList<string> ids)
        => _disabledSkillIds = [.. ids];

    // ── 行为方法：SubAgent 权限（O-2-4）──────────────────────────────────

    /// <summary>
    /// 检查是否允许调用指定子代理。
    /// null 白名单 = 允许调用所有；空列表 = 禁止调用任何；具体 ID 列表 = 仅允许指定 ID。
    /// </summary>
    public bool CanCallSubAgent(string agentId)
    {
        if (_allowedSubAgentIds is null) return true;        // null = 全允许
        if (_allowedSubAgentIds.Count == 0) return false;   // 空列表 = 全禁止
        return _allowedSubAgentIds.Contains(agentId);
    }

    /// <summary>更新允许调用的子代理白名单（null = 全允许，空列表 = 全禁止）。</summary>
    public void UpdateAllowedSubAgentIds(IReadOnlyList<string>? ids)
        => _allowedSubAgentIds = ids is null ? null : [.. ids];

    // ── DTO 转换（O-2-9）─────────────────────────────────────────────────

    /// <summary>转换为 AgentConfig DTO（用于 API 响应，保持向后兼容）。</summary>
    public AgentConfig ToConfig() => new(
        Id: Id,
        Name: Name,
        Description: Description,
        IsEnabled: IsEnabled,
        DisabledSkillIds: _disabledSkillIds.AsReadOnly(),
        DisabledMcpServerIds: _disabledMcpServerIds.AsReadOnly(),
        ToolGroupConfigs: _toolGroupConfigs.AsReadOnly(),
        CreatedAtUtc: CreatedAtUtc,
        IsDefault: IsDefault,
        ContextWindowMessages: ContextWindowMessages,
        ExposeAsA2A: ExposeAsA2A,
        AllowedSubAgentIds: _allowedSubAgentIds?.AsReadOnly(),
        RoutingStrategy: RoutingStrategy,
        MonthlyBudgetUsd: MonthlyBudgetUsd);

    /// <summary>
    /// 应用 Pet 工具覆盖，返回含覆盖配置的新 Agent 实例（不修改原对象）。
    /// 仅用于单次请求内的 ToolCollector 调用，不持久化。
    /// </summary>
    internal Agent WithToolOverrides(IReadOnlyList<ToolGroupConfig> overrides) =>
        Reconstitute(
            id: Id,
            name: Name,
            description: Description,
            isEnabled: IsEnabled,
            disabledSkillIds: _disabledSkillIds,
            disabledMcpServerIds: _disabledMcpServerIds,
            toolGroupConfigs: overrides,
            createdAtUtc: CreatedAtUtc,
            isDefault: IsDefault,
            contextWindowMessages: ContextWindowMessages,
            exposeAsA2A: ExposeAsA2A,
            allowedSubAgentIds: _allowedSubAgentIds,
            routingStrategy: RoutingStrategy,
            monthlyBudgetUsd: MonthlyBudgetUsd);
}
