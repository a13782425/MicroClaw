using FluentAssertions;
using MicroClaw.Safety;

namespace MicroClaw.Tests.Safety;

public class RiskLevelTests
{
    [Fact]
    public void RiskLevel_Ordinals_AreCorrect()
    {
        ((int)RiskLevel.Low).Should().Be(0);
        ((int)RiskLevel.Medium).Should().Be(1);
        ((int)RiskLevel.High).Should().Be(2);
        ((int)RiskLevel.Critical).Should().Be(3);
    }

    [Fact]
    public void RiskLevel_Critical_IsGreaterThanHigh()
    {
        (RiskLevel.Critical > RiskLevel.High).Should().BeTrue();
        (RiskLevel.High > RiskLevel.Medium).Should().BeTrue();
        (RiskLevel.Medium > RiskLevel.Low).Should().BeTrue();
    }

    [Fact]
    public void RiskLevel_HasFourValues()
    {
        Enum.GetValues<RiskLevel>().Should().HaveCount(4);
    }
}
