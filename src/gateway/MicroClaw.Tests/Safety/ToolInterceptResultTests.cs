using FluentAssertions;
using MicroClaw.Safety;

namespace MicroClaw.Tests.Safety;

public class ToolInterceptResultTests
{
    [Fact]
    public void Allow_IsAllowedTrue_BlockReasonNull()
    {
        var result = ToolInterceptResult.Allow();

        result.IsAllowed.Should().BeTrue();
        result.BlockReason.Should().BeNull();
    }

    [Fact]
    public void Block_IsAllowedFalse_BlockReasonSet()
    {
        var result = ToolInterceptResult.Block("危险操作");

        result.IsAllowed.Should().BeFalse();
        result.BlockReason.Should().Be("危险操作");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Block_EmptyReason_Throws(string? reason)
    {
        var act = () => ToolInterceptResult.Block(reason!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allow_IsImmutableRecord_TwoInstancesAreEqual()
    {
        var a = ToolInterceptResult.Allow();
        var b = ToolInterceptResult.Allow();
        a.Should().Be(b);
    }
}
