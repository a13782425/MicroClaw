using FluentAssertions;
using MicroClaw.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroClaw.Tests.Safety;

public class LoggingToolRiskInterceptorTests
{
    private readonly LoggingToolRiskInterceptor _interceptor =
        new(NullLogger<LoggingToolRiskInterceptor>.Instance);

    // ── 构造参数校验 ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new LoggingToolRiskInterceptor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── 各风险等级均返回 Allow ─────────────────────────────────────────────

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public async Task InterceptAsync_AnyRiskLevel_ReturnsAllow(RiskLevel level)
    {
        var result = await _interceptor.InterceptAsync("some_tool", level, null);

        result.IsAllowed.Should().BeTrue();
        result.BlockReason.Should().BeNull();
    }

    [Fact]
    public async Task InterceptAsync_WithArgs_StillAllows()
    {
        var args = new Dictionary<string, object?> { ["command"] = "echo hello" };
        var result = await _interceptor.InterceptAsync("exec_command", RiskLevel.Critical, args);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task InterceptAsync_IsCancellable()
    {
        using var cts = new CancellationTokenSource();
        // 不取消时应正常返回
        var result = await _interceptor.InterceptAsync("read_file", RiskLevel.Low, null, cts.Token);
        result.IsAllowed.Should().BeTrue();
    }
}
