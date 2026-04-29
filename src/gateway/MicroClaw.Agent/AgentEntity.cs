using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;

namespace MicroClaw.Agent;
/// <summary>
/// Agent 实体（领域对象）：聚合工具权限、子代理策略、路由配置等行为。
/// </summary>
public sealed class AgentEntity
{
    private List<string> _disabledSkillIds = [];
    private List<string> _disabledMcpServerIds = [];
    private List<ToolGroupConfig> _toolGroupConfigs = [];
    private List<string>? _allowedSubAgentIds;
    private readonly AgentEntityConfig _config;
    private ProviderRoutingStrategy _routingStrategy = default;
    public AgentEntity(AgentEntityConfig config)
    {
        _config = config;
        _disabledSkillIds = MicroClawUtils.DeserializeList<string>(config.DisabledSkillIdsJson);
        _disabledMcpServerIds = MicroClawUtils.DeserializeList<string>(config.DisabledMcpServerIdsJson);
        _toolGroupConfigs = MicroClawUtils.DeserializeList<ToolGroupConfig>(config.ToolGroupConfigsJson);
        _allowedSubAgentIds = config.AllowedSubAgentIdsJson is null ? null : MicroClawUtils.DeserializeList<string>(config.AllowedSubAgentIdsJson);
        _routingStrategy = AgentUtils.ParseRoutingStrategy(config.RoutingStrategy);
    }
    
    // ── 属性 ─────────────────────────────────────────────────────────────
    
    internal AgentEntityConfig Config => _config;
    
    public string Id => _config.Id;
    public string Name
    {
        get => _config.Name;
        set => _config.Name = value;
    }
    public string Description
    {
        get => _config.Description;
        set => _config.Description = value;
    }
    public bool IsEnabled
    {
        get => _config.IsEnabled;
        set => _config.IsEnabled = value;
    }
    public bool IsDefault
    {
        get => _config.IsDefault;
        set => _config.IsDefault = value;
    }
    public DateTimeOffset CreatedAtUtc => TimeUtils.FromMs(_config.CreatedAtMs);
    public int? ContextWindowMessages
    {
        get => _config.ContextWindowMessages;
        set => _config.ContextWindowMessages = value;
    }
    public ProviderRoutingStrategy RoutingStrategy
    {
        get => _routingStrategy;
        set => _config.RoutingStrategy = value == ProviderRoutingStrategy.Default ? null : value.ToString();
    }
    public decimal? MonthlyBudgetUsd
    {
        get => _config.MonthlyBudgetUsd;
        set => _config.MonthlyBudgetUsd = value;
    }
    
    public IReadOnlyList<string> DisabledSkillIds => _disabledSkillIds.AsReadOnly();
    public IReadOnlyList<string> DisabledMcpServerIds => _disabledMcpServerIds.AsReadOnly();
    public IReadOnlyList<ToolGroupConfig> ToolGroupConfigs => _toolGroupConfigs.AsReadOnly();
    public IReadOnlyList<string>? AllowedSubAgentIds => _allowedSubAgentIds?.AsReadOnly();
    
    // ── 工厂方法 ──────────────────────────────────────────────────────────
    public static AgentEntity New(string name, string description, bool isEnabled = true, int? contextWindowMessages = null) =>
        new(new AgentEntityConfig
        {
            Id = MicroClawUtils.GetUniqueId(),
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            CreatedAtMs = TimeUtils.ToMs(DateTimeOffset.UtcNow),
            ContextWindowMessages = contextWindowMessages,
        });
    
    // ── 行为方法：生命周期 ────────────────────────────────────────────────
    
    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
    
    public void UpdateInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }
    
    public void UpdateContextWindow(int? contextWindowMessages) => ContextWindowMessages = contextWindowMessages;
    
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
    public void UpdateToolGroupConfigs(IReadOnlyList<ToolGroupConfig> configs) => _toolGroupConfigs = [.. configs];
    
    // ── 行为方法：MCP/Skill 禁用管理（O-2-3）────────────────────────────
    
    /// <summary>检查指定 MCP Server 是否被禁用（按 Id 或 Name 匹配）。</summary>
    public bool IsMcpServerDisabled(string serverIdOrName) => _disabledMcpServerIds.Contains(serverIdOrName);
    
    /// <summary>更新禁用的 MCP Server 列表。</summary>
    public void UpdateDisabledMcpServerIds(IReadOnlyList<string> ids) => _disabledMcpServerIds = [.. ids];
    
    /// <summary>检查指定 Skill 是否被禁用。</summary>
    public bool IsSkillDisabled(string skillId) => _disabledSkillIds.Contains(skillId);
    
    /// <summary>更新禁用的 Skill 列表。</summary>
    public void UpdateDisabledSkillIds(IReadOnlyList<string> ids) => _disabledSkillIds = [.. ids];
    
    // ── 行为方法：SubAgent 权限（O-2-4）──────────────────────────────────
    
    /// <summary>
    /// 检查是否允许调用指定子代理。
    /// null 白名单 = 允许调用所有；空列表 = 禁止调用任何；具体 ID 列表 = 仅允许指定 ID。
    /// </summary>
    public bool CanCallSubAgent(string agentId)
    {
        if (_allowedSubAgentIds is null) return true; // null = 全允许
        if (_allowedSubAgentIds.Count == 0) return false; // 空列表 = 全禁止
        return _allowedSubAgentIds.Contains(agentId);
    }
    
    /// <summary>更新允许调用的子代理白名单（null = 全允许，空列表 = 全禁止）。</summary>
    public void UpdateAllowedSubAgentIds(IReadOnlyList<string>? ids) => _allowedSubAgentIds = ids is null ? null : [.. ids];
    
}