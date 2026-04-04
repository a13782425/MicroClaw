using FluentAssertions;
using MicroClaw.Pet.Decision;
using MicroClaw.Tools;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetDispatchResult 模型单元测试：验证 record 属性、默认值和不可变性。
/// </summary>
public sealed class PetDispatchResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new PetDispatchResult();

        result.AgentId.Should().BeNull();
        result.ProviderId.Should().BeNull();
        result.ToolOverrides.Should().BeEmpty();
        result.PetKnowledge.Should().BeNull();
        result.ShouldPetRespond.Should().BeFalse();
        result.PetResponse.Should().BeNull();
        result.Reason.Should().BeEmpty();
    }

    [Fact]
    public void WithInit_SetsAllProperties()
    {
        var overrides = new List<ToolGroupConfig>
        {
            new("cron", true, []),
            new("mcp-server-1", false, ["tool-a"]),
        };

        var result = new PetDispatchResult
        {
            AgentId = "agent-1",
            ProviderId = "provider-1",
            ToolOverrides = overrides,
            PetKnowledge = "用户偏好使用中文回复",
            ShouldPetRespond = false,
            Reason = "代码相关问题委派给 coding agent",
        };

        result.AgentId.Should().Be("agent-1");
        result.ProviderId.Should().Be("provider-1");
        result.ToolOverrides.Should().HaveCount(2);
        result.PetKnowledge.Should().Contain("中文");
        result.ShouldPetRespond.Should().BeFalse();
        result.Reason.Should().Contain("coding agent");
    }

    [Fact]
    public void ShouldPetRespond_WithPetResponse()
    {
        var result = new PetDispatchResult
        {
            ShouldPetRespond = true,
            PetResponse = "我目前正在学习，稍后为你处理。",
            Reason = "Pet 处于学习状态，暂时自己回复",
        };

        result.ShouldPetRespond.Should().BeTrue();
        result.PetResponse.Should().NotBeNullOrWhiteSpace();
        result.AgentId.Should().BeNull(); // 不委派 Agent
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        var original = new PetDispatchResult
        {
            AgentId = "agent-1",
            Reason = "original reason",
        };

        var modified = original with { AgentId = "agent-2", Reason = "modified reason" };

        original.AgentId.Should().Be("agent-1");
        modified.AgentId.Should().Be("agent-2");
        modified.Reason.Should().Be("modified reason");
    }
}
