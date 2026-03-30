using FluentAssertions;
using MicroClaw.Safety;

namespace MicroClaw.Tests.Safety;

public class ToolListConfigTests
{
    // ── 构造验证 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_HasNoWhitelistOrGraylist()
    {
        var config = ToolListConfig.Empty;

        config.WhitelistedTools.Should().BeEmpty();
        config.GreylistedTools.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_AcceptsEmptyLists()
    {
        var config = new ToolListConfig([], []);

        config.WhitelistedTools.Should().BeEmpty();
        config.GreylistedTools.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenToolInBothLists()
    {
        Action act = () => new ToolListConfig(
            ["read_file", "write_file"],
            ["write_file", "exec_command"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*write_file*");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenWhitelistIsNull()
    {
        Action act = () => new ToolListConfig(null!, []);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenGraylistIsNull()
    {
        Action act = () => new ToolListConfig([], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsWhitelisted ─────────────────────────────────────────────────────────

    [Fact]
    public void IsWhitelisted_ReturnsTrue_ForWhitelistedTool()
    {
        var config = new ToolListConfig(["read_file", "list_directory"], []);

        config.IsWhitelisted("read_file").Should().BeTrue();
        config.IsWhitelisted("list_directory").Should().BeTrue();
    }

    [Fact]
    public void IsWhitelisted_IsCaseInsensitive()
    {
        var config = new ToolListConfig(["Read_File"], []);

        config.IsWhitelisted("read_file").Should().BeTrue();
        config.IsWhitelisted("READ_FILE").Should().BeTrue();
        config.IsWhitelisted("Read_File").Should().BeTrue();
    }

    [Fact]
    public void IsWhitelisted_ReturnsFalse_ForUnknownTool()
    {
        var config = new ToolListConfig(["read_file"], []);

        config.IsWhitelisted("write_file").Should().BeFalse();
        config.IsWhitelisted("exec_command").Should().BeFalse();
    }

    [Fact]
    public void IsWhitelisted_ReturnsFalse_ForGreylistedTool()
    {
        var config = new ToolListConfig([], ["exec_command"]);

        config.IsWhitelisted("exec_command").Should().BeFalse();
    }

    // ── IsGreylisted ──────────────────────────────────────────────────────────

    [Fact]
    public void IsGreylisted_ReturnsTrue_ForGreylistedTool()
    {
        var config = new ToolListConfig([], ["exec_command", "write_file"]);

        config.IsGreylisted("exec_command").Should().BeTrue();
        config.IsGreylisted("write_file").Should().BeTrue();
    }

    [Fact]
    public void IsGreylisted_IsCaseInsensitive()
    {
        var config = new ToolListConfig([], ["Exec_Command"]);

        config.IsGreylisted("exec_command").Should().BeTrue();
        config.IsGreylisted("EXEC_COMMAND").Should().BeTrue();
    }

    [Fact]
    public void IsGreylisted_ReturnsFalse_ForWhitelistedTool()
    {
        var config = new ToolListConfig(["read_file"], []);

        config.IsGreylisted("read_file").Should().BeFalse();
    }

    [Fact]
    public void IsGreylisted_ReturnsFalse_ForUnknownTool()
    {
        var config = new ToolListConfig([], ["exec_command"]);

        config.IsGreylisted("fetch_url").Should().BeFalse();
    }

    // ── 属性验证 ──────────────────────────────────────────────────────────────

    [Fact]
    public void WhitelistedTools_ReturnsAllConfiguredTools()
    {
        var config = new ToolListConfig(["read_file", "list_directory"], []);

        config.WhitelistedTools.Should().HaveCount(2);
        config.WhitelistedTools.Should().Contain("read_file");
        config.WhitelistedTools.Should().Contain("list_directory");
    }

    [Fact]
    public void GreylistedTools_ReturnsAllConfiguredTools()
    {
        var config = new ToolListConfig([], ["exec_command", "write_file"]);

        config.GreylistedTools.Should().HaveCount(2);
        config.GreylistedTools.Should().Contain("exec_command");
        config.GreylistedTools.Should().Contain("write_file");
    }

    [Fact]
    public void Constructor_TrimsWhitespace_FromToolNames()
    {
        var config = new ToolListConfig(["  read_file  "], ["  exec_command  "]);

        config.IsWhitelisted("read_file").Should().BeTrue();
        config.IsGreylisted("exec_command").Should().BeTrue();
    }
}
