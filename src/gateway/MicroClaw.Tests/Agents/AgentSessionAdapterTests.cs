using FluentAssertions;
using MicroClaw.Agent.Sessions;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using Microsoft.Agents.AI;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// AgentSessionAdapter 单元测试
/// 覆盖 StateBag 填写、键读取、null SessionInfo 分支等场景
/// </summary>
public sealed class AgentSessionAdapterTests
{
    // ── PopulateStateBag ──────────────────────────────────────────────────

    [Fact]
    public void PopulateStateBag_WithFullSessionInfo_SetsAllCoreKeys()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("sess-1", "agent-99", "provider-A");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        bag.Count.Should().Be(6, "sessionId, providerId, channelType, channelId, title, agentId");
    }

    [Fact]
    public void PopulateStateBag_SessionIdKey_MatchesSessionInfoId()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("sess-abc", "agent-1", "prov-1");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeySessionId)
            .Should().Be("sess-abc");
    }

    [Fact]
    public void PopulateStateBag_ProviderIdKey_MatchesSessionInfoProviderId()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("s", "a", "my-provider");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyProviderId)
            .Should().Be("my-provider");
    }

    [Fact]
    public void PopulateStateBag_AgentIdKey_MatchesSessionInfoAgentId()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("s", "agent-xyz", "p");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyAgentId)
            .Should().Be("agent-xyz");
    }

    [Fact]
    public void PopulateStateBag_ChannelTypeKey_ContainsChannelTypeString()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("s", "a", "p", channelType: ChannelType.Web);

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyChannelType)
            .Should().Be("Web");
    }

    [Fact]
    public void PopulateStateBag_ChannelIdKey_MatchesSessionInfoChannelId()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("s", "a", "p", channelId: "chan-99");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyChannelId)
            .Should().Be("chan-99");
    }

    [Fact]
    public void PopulateStateBag_TitleKey_MatchesSessionInfoTitle()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("s", "a", "p", title: "My Test Session");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyTitle)
            .Should().Be("My Test Session");
    }

    [Fact]
    public void PopulateStateBag_NullAgentId_DoesNotSetAgentIdKey()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = new(
            Id: "s",
            Title: "t",
            ProviderId: "p",
            IsApproved: true,
            ChannelType: ChannelType.Web,
            ChannelId: "c",
            CreatedAt: DateTimeOffset.UtcNow,
            AgentId: null);

        AgentSessionAdapter.PopulateStateBag(bag, info);

        bag.Count.Should().Be(5, "agentId 为 null 时不写入 StateBag");
        bool found = bag.TryGetValue<string>(AgentSessionAdapter.KeyAgentId, out _, new());
        found.Should().BeFalse();
    }

    [Fact]
    public void PopulateStateBag_CalledTwice_OverwritesPreviousValues()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo first = BuildSessionInfo("sess-1", "agent-A", "prov-X", title: "First");
        SessionInfo second = BuildSessionInfo("sess-2", "agent-B", "prov-Y", title: "Second");

        AgentSessionAdapter.PopulateStateBag(bag, first);
        AgentSessionAdapter.PopulateStateBag(bag, second);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeySessionId)
            .Should().Be("sess-2");
        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyTitle)
            .Should().Be("Second");
    }

    // ── GetStringValue ────────────────────────────────────────────────────

    [Fact]
    public void GetStringValue_NonExistentKey_ReturnsNull()
    {
        var bag = new AgentSessionStateBag();

        string? result = AgentSessionAdapter.GetStringValue(bag, "does.not.exist");

        result.Should().BeNull();
    }

    [Fact]
    public void GetStringValue_ExistingKey_ReturnsStoredValue()
    {
        var bag = new AgentSessionStateBag();
        SessionInfo info = BuildSessionInfo("sess-read", "a", "p");

        AgentSessionAdapter.PopulateStateBag(bag, info);

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeySessionId)
            .Should().Be("sess-read");
    }

    [Fact]
    public void GetStringValue_EmptyBag_ReturnsNull()
    {
        var bag = new AgentSessionStateBag();

        AgentSessionAdapter.GetStringValue(bag, AgentSessionAdapter.KeyAgentId)
            .Should().BeNull();
    }

    // ── Key 常量完整性验证 ──────────────────────────────────────────────

    [Fact]
    public void KeyConstants_AllHaveMcPrefix()
    {
        new[]
        {
            AgentSessionAdapter.KeySessionId,
            AgentSessionAdapter.KeyProviderId,
            AgentSessionAdapter.KeyAgentId,
            AgentSessionAdapter.KeyChannelType,
            AgentSessionAdapter.KeyChannelId,
            AgentSessionAdapter.KeyTitle
        }.Should().AllSatisfy(k => k.Should().StartWith("mc."));
    }

    [Fact]
    public void KeyConstants_AreAllUnique()
    {
        var keys = new[]
        {
            AgentSessionAdapter.KeySessionId,
            AgentSessionAdapter.KeyProviderId,
            AgentSessionAdapter.KeyAgentId,
            AgentSessionAdapter.KeyChannelType,
            AgentSessionAdapter.KeyChannelId,
            AgentSessionAdapter.KeyTitle
        };

        keys.Should().OnlyHaveUniqueItems();
    }

    // ── 辅助方法 ────────────────────────────────────────────────────────

    private static SessionInfo BuildSessionInfo(
        string id,
        string agentId,
        string providerId,
        string channelId = "chan-1",
        string title = "Test Session",
        ChannelType channelType = ChannelType.Web)
        => new(
            Id: id,
            Title: title,
            ProviderId: providerId,
            IsApproved: true,
            ChannelType: channelType,
            ChannelId: channelId,
            CreatedAt: DateTimeOffset.UtcNow,
            AgentId: agentId);
}
