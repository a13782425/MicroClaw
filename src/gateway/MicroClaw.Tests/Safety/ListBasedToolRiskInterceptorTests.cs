using FluentAssertions;
using MicroClaw.Safety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Safety;

public class ListBasedToolRiskInterceptorTests
{
    private static ListBasedToolRiskInterceptor CreateInterceptor(IToolListConfig listConfig)
    {
        var innerLogger = NullLogger<LoggingToolRiskInterceptor>.Instance;
        var outerLogger = NullLogger<ListBasedToolRiskInterceptor>.Instance;
        return new ListBasedToolRiskInterceptor(listConfig, innerLogger, outerLogger);
    }

    // ── 构造验证 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenListConfigIsNull()
    {
        var innerLogger = NullLogger<LoggingToolRiskInterceptor>.Instance;
        var outerLogger = NullLogger<ListBasedToolRiskInterceptor>.Instance;

        Action act = () => new ListBasedToolRiskInterceptor(null!, innerLogger, outerLogger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("listConfig");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenInnerLoggerIsNull()
    {
        var outerLogger = NullLogger<ListBasedToolRiskInterceptor>.Instance;

        Action act = () => new ListBasedToolRiskInterceptor(ToolListConfig.Empty, null!, outerLogger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerLogger");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        var innerLogger = NullLogger<LoggingToolRiskInterceptor>.Instance;

        Action act = () => new ListBasedToolRiskInterceptor(ToolListConfig.Empty, innerLogger, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── 白名单行为 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Intercept_ReturnsAllow_ForWhitelistedTool()
    {
        var config = new ToolListConfig(["read_file", "list_directory"], []);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("read_file", RiskLevel.Low, null);

        result.IsAllowed.Should().BeTrue();
        result.BlockReason.Should().BeNull();
    }

    [Fact]
    public async Task Intercept_AllowsWhitelisted_EvenWhenHighRisk()
    {
        // 白名单工具即使风险等级高也直接放行（免检）
        var config = new ToolListConfig(["exec_command"], []);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("exec_command", RiskLevel.Critical, null);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Intercept_WhitelistIsCaseInsensitive()
    {
        var config = new ToolListConfig(["Read_File"], []);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("read_file", RiskLevel.Low, null);
        result.IsAllowed.Should().BeTrue();
    }

    // ── 灰名单行为 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Intercept_ReturnsBlock_ForGreylistedTool()
    {
        var config = new ToolListConfig([], ["exec_command"]);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("exec_command", RiskLevel.Critical, null);

        result.IsAllowed.Should().BeFalse();
        result.BlockReason.Should().NotBeNullOrEmpty();
        result.BlockReason.Should().Contain("exec_command");
        result.BlockReason.Should().Contain("灰名单");
    }

    [Fact]
    public async Task Intercept_BlockReason_MentionsToolName()
    {
        var config = new ToolListConfig([], ["write_file"]);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("write_file", RiskLevel.High, null);

        result.BlockReason.Should().Contain("write_file");
    }

    [Fact]
    public async Task Intercept_GreylistIsCaseInsensitive()
    {
        var config = new ToolListConfig([], ["Exec_Command"]);
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("exec_command", RiskLevel.Critical, null);
        result.IsAllowed.Should().BeFalse();
    }

    // ── 默认行为（不在任何列表中）──────────────────────────────────────────────

    [Fact]
    public async Task Intercept_Allows_ForUnlistedTool()
    {
        // 不在白名单或灰名单的工具，委托给 LoggingToolRiskInterceptor（始终放行）
        var config = ToolListConfig.Empty;
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("fetch_url", RiskLevel.Medium, null);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Intercept_Allows_ForUnlistedHighRiskTool()
    {
        var config = ToolListConfig.Empty;
        var interceptor = CreateInterceptor(config);

        ToolInterceptResult result = await interceptor.InterceptAsync("exec_command", RiskLevel.Critical, null);

        // 不在灰名单时，Critical 级别的工具也被 LoggingToolRiskInterceptor 放行（允许）
        result.IsAllowed.Should().BeTrue();
    }

    // ── 混合配置验证 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Intercept_CorrectlyHandles_MixedConfig()
    {
        var config = new ToolListConfig(
            whitelistedTools: ["read_file", "list_directory"],
            greylistedTools: ["exec_command", "write_file"]);
        var interceptor = CreateInterceptor(config);

        // 白名单工具放行
        (await interceptor.InterceptAsync("read_file", RiskLevel.Low, null))
            .IsAllowed.Should().BeTrue();

        // 灰名单工具阻止
        (await interceptor.InterceptAsync("exec_command", RiskLevel.Critical, null))
            .IsAllowed.Should().BeFalse();

        // 未在列表中的工具放行
        (await interceptor.InterceptAsync("fetch_url", RiskLevel.Medium, null))
            .IsAllowed.Should().BeTrue();
    }

    // ── IToolListConfig mock 验证 ─────────────────────────────────────────────

    [Fact]
    public async Task Intercept_QueriesListConfig_WithCorrectToolName()
    {
        var mockConfig = Substitute.For<IToolListConfig>();
        mockConfig.IsWhitelisted("read_file").Returns(false);
        mockConfig.IsGreylisted("read_file").Returns(false);

        var interceptor = CreateInterceptor(mockConfig);
        await interceptor.InterceptAsync("read_file", RiskLevel.Low, null);

        mockConfig.Received(1).IsWhitelisted("read_file");
    }

    [Fact]
    public async Task Intercept_DoesNotQueryGraylist_WhenToolIsWhitelisted()
    {
        // 白名单命中后不应再查询灰名单（短路求值）
        var mockConfig = Substitute.For<IToolListConfig>();
        mockConfig.IsWhitelisted("read_file").Returns(true);

        var interceptor = CreateInterceptor(mockConfig);
        await interceptor.InterceptAsync("read_file", RiskLevel.Low, null);

        mockConfig.DidNotReceive().IsGreylisted(Arg.Any<string>());
    }
}
