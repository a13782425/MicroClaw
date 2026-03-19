using FluentAssertions;
using MicroClaw.Providers;

namespace MicroClaw.Tests.Providers;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        var registry = new ProviderRegistry();

        registry.All.Should().BeEmpty();
    }

    [Fact]
    public void Register_AddsProvider()
    {
        var registry = new ProviderRegistry();
        var info = new ProviderInfo("gpt-4o", "gpt-4o", "openai-key");

        registry.Register(info);

        registry.All.Should().ContainSingle()
            .Which.Should().Be(info);
    }

    [Fact]
    public void Register_MultipleTimes_AddsAll()
    {
        var registry = new ProviderRegistry();
        var info1 = new ProviderInfo("gpt-4o", "gpt-4o", "openai-key");
        var info2 = new ProviderInfo("claude-4", "claude-4-sonnet", "anthropic-key");

        registry.Register(info1);
        registry.Register(info2);

        registry.All.Should().HaveCount(2);
        registry.All.Should().Contain(info1);
        registry.All.Should().Contain(info2);
    }

    [Fact]
    public void Register_DuplicateInfo_AddsAnotherEntry()
    {
        var registry = new ProviderRegistry();
        var info = new ProviderInfo("gpt-4o", "gpt-4o", "openai-key");

        registry.Register(info);
        registry.Register(info);

        registry.All.Should().HaveCount(2);
    }
}
