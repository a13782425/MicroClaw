using FluentAssertions;
using MicroClaw.RAG;

namespace MicroClaw.Tests.RAG;

public class RagSkeletonTests
{
    [Fact]
    public void RagScope_Should_Have_Global_And_Session_Values()
    {
        Enum.GetValues<RagScope>().Should().BeEquivalentTo([RagScope.Global, RagScope.Session]);
    }

    [Fact]
    public void RagConfig_Should_Construct_With_All_Properties()
    {
        var now = DateTime.UtcNow;
        var config = new RagConfig(
            Id: "test-1",
            Name: "Test KB",
            Scope: RagScope.Global,
            SessionId: null,
            SourceType: "document",
            IsEnabled: true,
            CreatedAtUtc: now);

        config.Id.Should().Be("test-1");
        config.Name.Should().Be("Test KB");
        config.Scope.Should().Be(RagScope.Global);
        config.SessionId.Should().BeNull();
        config.SourceType.Should().Be("document");
        config.IsEnabled.Should().BeTrue();
        config.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public void RagConfig_Session_Scope_Should_Have_SessionId()
    {
        var config = new RagConfig(
            Id: "s-1",
            Name: "Session KB",
            Scope: RagScope.Session,
            SessionId: "session-abc",
            SourceType: "conversation",
            IsEnabled: true,
            CreatedAtUtc: DateTime.UtcNow);

        config.Scope.Should().Be(RagScope.Session);
        config.SessionId.Should().Be("session-abc");
    }

    [Fact]
    public void IRagService_Should_Be_Discoverable_Interface()
    {
        typeof(IRagService).IsInterface.Should().BeTrue();
        typeof(IRagService).GetMethods().Should().Contain(m => m.Name == "QueryAsync");
        typeof(IRagService).GetMethods().Should().Contain(m => m.Name == "IngestAsync");
    }
}
