using FluentAssertions;
using MicroClaw.Safety;

namespace MicroClaw.Tests.Safety;

public class DefaultToolRiskRegistryTests
{
    private readonly DefaultToolRiskRegistry _registry = new();

    // ── 内置工具风险等级 ───────────────────────────────────────────────────

    [Fact]
    public void GetRiskLevel_ExecCommand_ReturnsCritical()
        => _registry.GetRiskLevel("exec_command").Should().Be(RiskLevel.Critical);

    [Theory]
    [InlineData("read_file")]
    [InlineData("list_directory")]
    [InlineData("search_files")]
    [InlineData("list_cron_jobs")]
    [InlineData("get_current_time")]
    public void GetRiskLevel_ReadOnlyTools_ReturnsLow(string toolName)
        => _registry.GetRiskLevel(toolName).Should().Be(RiskLevel.Low);

    [Theory]
    [InlineData("write_file")]
    [InlineData("edit_file")]
    public void GetRiskLevel_WriteFileTools_ReturnsHigh(string toolName)
        => _registry.GetRiskLevel(toolName).Should().Be(RiskLevel.High);

    [Theory]
    [InlineData("fetch_url")]
    [InlineData("create_cron_job")]
    [InlineData("update_cron_job")]
    [InlineData("delete_cron_job")]
    public void GetRiskLevel_MediumTools_ReturnsMedium(string toolName)
        => _registry.GetRiskLevel(toolName).Should().Be(RiskLevel.Medium);

    // ── 未知工具 ──────────────────────────────────────────────────────────

    [Fact]
    public void GetRiskLevel_UnknownTool_ReturnsLow()
        => _registry.GetRiskLevel("some_mcp_tool_xyz").Should().Be(RiskLevel.Low);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRiskLevel_EmptyOrNull_ReturnsLow(string? toolName)
        => _registry.GetRiskLevel(toolName!).Should().Be(RiskLevel.Low);

    // ── 大小写不敏感 ───────────────────────────────────────────────────────

    [Fact]
    public void GetRiskLevel_CaseInsensitive()
    {
        _registry.GetRiskLevel("EXEC_COMMAND").Should().Be(RiskLevel.Critical);
        _registry.GetRiskLevel("Exec_Command").Should().Be(RiskLevel.Critical);
    }

    // ── 自定义标注 ─────────────────────────────────────────────────────────

    [Fact]
    public void CustomAnnotations_OverrideBuiltin()
    {
        var custom = new List<ToolRiskAnnotation>
        {
            new("exec_command", RiskLevel.Low, "测试用：降低 shell 风险"),
        };
        var registry = new DefaultToolRiskRegistry(custom);
        registry.GetRiskLevel("exec_command").Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void CustomAnnotations_AddNewTool()
    {
        var custom = new List<ToolRiskAnnotation>
        {
            new("my_custom_tool", RiskLevel.High, "自定义高风险工具"),
        };
        var registry = new DefaultToolRiskRegistry(custom);
        registry.GetRiskLevel("my_custom_tool").Should().Be(RiskLevel.High);
    }

    // ── GetAllAnnotations ────────────────────────────────────────────────

    [Fact]
    public void GetAllAnnotations_ReturnsNonEmpty()
        => _registry.GetAllAnnotations().Should().NotBeEmpty();

    [Fact]
    public void GetAllAnnotations_ContainsExecCommand()
    {
        _registry.GetAllAnnotations()
            .Should().Contain(a => a.ToolName == "exec_command" && a.RiskLevel == RiskLevel.Critical);
    }
}
