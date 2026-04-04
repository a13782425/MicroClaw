using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Tools;
using AgentEntity = MicroClaw.Agent.Agent;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// Agent 领域对象单元测试（O-2-11）：
/// 验证工具权限、MCP/Skill 禁用、子代理权限、生命周期行为和 DTO 转换。
/// </summary>
public sealed class AgentDomainObjectTests
{
    private static AgentEntity BuildAgent(
        string id = "a1",
        string name = "Test",
        bool isEnabled = true,
        List<string>? disabledSkillIds = null,
        List<string>? disabledMcpServerIds = null,
        List<ToolGroupConfig>? toolGroupConfigs = null,
        List<string>? allowedSubAgentIds = null,
        bool isDefault = false) =>
        AgentEntity.Reconstitute(
            id: id,
            name: name,
            description: "desc",
            isEnabled: isEnabled,
            disabledSkillIds: disabledSkillIds ?? [],
            disabledMcpServerIds: disabledMcpServerIds ?? [],
            toolGroupConfigs: toolGroupConfigs ?? [],
            createdAtUtc: DateTimeOffset.UtcNow,
            isDefault: isDefault,
            allowedSubAgentIds: allowedSubAgentIds);

    // ── 工厂方法 ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsEmptyIdAndName()
    {
        var agent = AgentEntity.Create(name: "MyAgent", description: "d", isEnabled: true);

        agent.Id.Should().BeEmpty();
        agent.Name.Should().Be("MyAgent");
        agent.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Reconstitute_RestoresAllProperties()
    {
        var createdAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var agent = AgentEntity.Reconstitute(
            id: "id1",
            name: "Restored",
            description: "desc",
            isEnabled: false,
            disabledSkillIds: ["skill-a"],
            disabledMcpServerIds: ["mcp-1"],
            toolGroupConfigs: [],
            createdAtUtc: createdAt,
            isDefault: true,
            contextWindowMessages: 20);

        agent.Id.Should().Be("id1");
        agent.Name.Should().Be("Restored");
        agent.IsEnabled.Should().BeFalse();
        agent.IsDefault.Should().BeTrue();
        agent.CreatedAtUtc.Should().Be(createdAt);
        agent.ContextWindowMessages.Should().Be(20);
        agent.DisabledSkillIds.Should().ContainSingle("skill-a");
        agent.DisabledMcpServerIds.Should().ContainSingle("mcp-1");
    }

    // ── 工具组权限（O-2-2）──────────────────────────────────────────────

    [Fact]
    public void IsToolGroupEnabled_NoConfig_ReturnsTrueByDefault()
    {
        var agent = BuildAgent();

        agent.IsToolGroupEnabled("any-group").Should().BeTrue();
    }

    [Fact]
    public void IsToolGroupEnabled_ConfigSetToTrue_ReturnsTrue()
    {
        var agent = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("grp1", IsEnabled: true, DisabledToolNames: [])
        ]);

        agent.IsToolGroupEnabled("grp1").Should().BeTrue();
    }

    [Fact]
    public void IsToolGroupEnabled_ConfigSetToFalse_ReturnsFalse()
    {
        var agent = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("grp1", IsEnabled: false, DisabledToolNames: [])
        ]);

        agent.IsToolGroupEnabled("grp1").Should().BeFalse();
    }

    [Fact]
    public void IsToolDisabled_ToolNotInConfig_ReturnsFalse()
    {
        var agent = BuildAgent();

        agent.IsToolDisabled("grp1", "toolA").Should().BeFalse();
    }

    [Fact]
    public void IsToolDisabled_ToolInDisabledList_ReturnsTrue()
    {
        var agent = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("grp1", IsEnabled: true, DisabledToolNames: ["toolA"])
        ]);

        agent.IsToolDisabled("grp1", "toolA").Should().BeTrue();
        agent.IsToolDisabled("grp1", "toolB").Should().BeFalse();
    }

    [Fact]
    public void UpdateToolGroupConfigs_ReplacesExistingConfigs()
    {
        var agent = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("old-group", IsEnabled: true, DisabledToolNames: [])
        ]);

        agent.UpdateToolGroupConfigs([
            new ToolGroupConfig("new-group", IsEnabled: false, DisabledToolNames: [])
        ]);

        agent.IsToolGroupEnabled("old-group").Should().BeTrue(); // 无配置 = 默认启用
        agent.IsToolGroupEnabled("new-group").Should().BeFalse();
    }

    // ── MCP Server 禁用（O-2-3）─────────────────────────────────────────

    [Fact]
    public void IsMcpServerDisabled_NotInList_ReturnsFalse()
    {
        var agent = BuildAgent();
        agent.IsMcpServerDisabled("mcpX").Should().BeFalse();
    }

    [Fact]
    public void IsMcpServerDisabled_InList_ReturnsTrue()
    {
        var agent = BuildAgent(disabledMcpServerIds: ["mcp-1", "mcp-2"]);
        agent.IsMcpServerDisabled("mcp-1").Should().BeTrue();
        agent.IsMcpServerDisabled("mcp-3").Should().BeFalse();
    }

    [Fact]
    public void UpdateDisabledMcpServerIds_ReplacesEntireList()
    {
        var agent = BuildAgent(disabledMcpServerIds: ["old"]);
        agent.UpdateDisabledMcpServerIds(["new-a", "new-b"]);

        agent.IsMcpServerDisabled("old").Should().BeFalse();
        agent.IsMcpServerDisabled("new-a").Should().BeTrue();
        agent.IsMcpServerDisabled("new-b").Should().BeTrue();
    }

    // ── Skill 禁用（O-2-3）──────────────────────────────────────────────

    [Fact]
    public void IsSkillDisabled_NotInList_ReturnsFalse()
    {
        var agent = BuildAgent();
        agent.IsSkillDisabled("skillX").Should().BeFalse();
    }

    [Fact]
    public void IsSkillDisabled_InList_ReturnsTrue()
    {
        var agent = BuildAgent(disabledSkillIds: ["skill-1"]);
        agent.IsSkillDisabled("skill-1").Should().BeTrue();
        agent.IsSkillDisabled("skill-2").Should().BeFalse();
    }

    [Fact]
    public void UpdateDisabledSkillIds_ReplacesEntireList()
    {
        var agent = BuildAgent(disabledSkillIds: ["old"]);
        agent.UpdateDisabledSkillIds(["new-x"]);

        agent.IsSkillDisabled("old").Should().BeFalse();
        agent.IsSkillDisabled("new-x").Should().BeTrue();
    }

    // ── SubAgent 权限（O-2-4）───────────────────────────────────────────

    [Fact]
    public void CanCallSubAgent_NullAllowedList_AllowsAll()
    {
        var agent = BuildAgent(allowedSubAgentIds: null);

        agent.CanCallSubAgent("any-id").Should().BeTrue();
        agent.CanCallSubAgent("another-id").Should().BeTrue();
    }

    [Fact]
    public void CanCallSubAgent_EmptyAllowedList_DeniesAll()
    {
        var agent = BuildAgent(allowedSubAgentIds: []);

        agent.CanCallSubAgent("any-id").Should().BeFalse();
    }

    [Fact]
    public void CanCallSubAgent_SpecificList_AllowsOnlyListed()
    {
        var agent = BuildAgent(allowedSubAgentIds: ["agent-a", "agent-b"]);

        agent.CanCallSubAgent("agent-a").Should().BeTrue();
        agent.CanCallSubAgent("agent-b").Should().BeTrue();
        agent.CanCallSubAgent("agent-c").Should().BeFalse();
    }

    [Fact]
    public void UpdateAllowedSubAgentIds_SetToNull_AllowsAll()
    {
        var agent = BuildAgent(allowedSubAgentIds: ["agent-a"]);
        agent.UpdateAllowedSubAgentIds(null);

        agent.CanCallSubAgent("any-id").Should().BeTrue();
    }

    [Fact]
    public void UpdateAllowedSubAgentIds_SetToEmpty_DeniesAll()
    {
        var agent = BuildAgent(allowedSubAgentIds: null);
        agent.UpdateAllowedSubAgentIds([]);

        agent.CanCallSubAgent("any-id").Should().BeFalse();
    }

    // ── 生命周期行为 ─────────────────────────────────────────────────────

    [Fact]
    public void Enable_SetsIsEnabledTrue()
    {
        var agent = BuildAgent(isEnabled: false);
        agent.Enable();
        agent.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Disable_SetsIsEnabledFalse()
    {
        var agent = BuildAgent(isEnabled: true);
        agent.Disable();
        agent.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateInfo_ChangesNameAndDescription()
    {
        var agent = BuildAgent(name: "OldName");
        agent.UpdateInfo("NewName", "NewDesc");

        agent.Name.Should().Be("NewName");
        agent.Description.Should().Be("NewDesc");
    }

    [Fact]
    public void UpdateContextWindow_SetsValue()
    {
        var agent = BuildAgent();
        agent.UpdateContextWindow(50);
        agent.ContextWindowMessages.Should().Be(50);
    }

    [Fact]
    public void UpdateContextWindow_SetToNull_ClearsValue()
    {
        var agent = AgentEntity.Reconstitute(
            id: "a1", name: "n", description: "d", isEnabled: true,
            disabledSkillIds: [], disabledMcpServerIds: [], toolGroupConfigs: [],
            createdAtUtc: DateTimeOffset.UtcNow, contextWindowMessages: 30);

        agent.UpdateContextWindow(null);
        agent.ContextWindowMessages.Should().BeNull();
    }

    // ── ToConfig 转换（O-2-9）────────────────────────────────────────────

    [Fact]
    public void ToConfig_RoundTrip_PreservesAllFields()
    {
        var original = AgentEntity.Reconstitute(
            id: "cfg-id",
            name: "Config Agent",
            description: "A description",
            isEnabled: true,
            disabledSkillIds: ["s1", "s2"],
            disabledMcpServerIds: ["m1"],
            toolGroupConfigs: [new ToolGroupConfig("g1", IsEnabled: false, DisabledToolNames: ["t1"])],
            createdAtUtc: new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
            isDefault: true,
            contextWindowMessages: 100,
            exposeAsA2A: true,
            allowedSubAgentIds: ["sub1"]);

        AgentConfig cfg = original.ToConfig();

        cfg.Id.Should().Be("cfg-id");
        cfg.Name.Should().Be("Config Agent");
        cfg.Description.Should().Be("A description");
        cfg.IsEnabled.Should().BeTrue();
        cfg.IsDefault.Should().BeTrue();
        cfg.ContextWindowMessages.Should().Be(100);
        cfg.ExposeAsA2A.Should().BeTrue();
        cfg.DisabledSkillIds.Should().BeEquivalentTo(["s1", "s2"]);
        cfg.DisabledMcpServerIds.Should().BeEquivalentTo(["m1"]);
        cfg.ToolGroupConfigs.Should().ContainSingle(g => g.GroupId == "g1" && !g.IsEnabled);
        cfg.AllowedSubAgentIds.Should().BeEquivalentTo(["sub1"]);
    }

    // ── WithToolOverrides（Pet 覆盖）────────────────────────────────────

    [Fact]
    public void WithToolOverrides_ReturnsNewInstance()
    {
        var original = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("grp1", IsEnabled: true, DisabledToolNames: [])
        ]);

        var overrideConfigs = new List<ToolGroupConfig>
        {
            new ToolGroupConfig("grp1", IsEnabled: false, DisabledToolNames: ["toolX"])
        };

        AgentEntity overridden = original.WithToolOverrides(overrideConfigs);

        overridden.Should().NotBeSameAs(original);
    }

    [Fact]
    public void WithToolOverrides_OriginalUnchanged()
    {
        var original = BuildAgent(toolGroupConfigs:
        [
            new ToolGroupConfig("grp1", IsEnabled: true, DisabledToolNames: [])
        ]);

        original.WithToolOverrides([
            new ToolGroupConfig("grp1", IsEnabled: false, DisabledToolNames: [])
        ]);

        // 原对象不受影响
        original.IsToolGroupEnabled("grp1").Should().BeTrue();
    }

    [Fact]
    public void WithToolOverrides_NewInstanceHasOverrideConfigs()
    {
        var original = BuildAgent();

        AgentEntity overridden = original.WithToolOverrides([
            new ToolGroupConfig("grp1", IsEnabled: false, DisabledToolNames: ["toolZ"])
        ]);

        overridden.IsToolGroupEnabled("grp1").Should().BeFalse();
        overridden.IsToolDisabled("grp1", "toolZ").Should().BeTrue();
    }

    [Fact]
    public void WithToolOverrides_PreservesOtherProperties()
    {
        var original = BuildAgent(
            id: "id99",
            name: "Agent99",
            isEnabled: true,
            disabledSkillIds: ["skill-x"]);

        AgentEntity overridden = original.WithToolOverrides([]);

        overridden.Id.Should().Be("id99");
        overridden.Name.Should().Be("Agent99");
        overridden.IsEnabled.Should().BeTrue();
        overridden.IsSkillDisabled("skill-x").Should().BeTrue();
    }
}
