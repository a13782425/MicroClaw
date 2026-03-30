using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.RAG;
using MicroClaw.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证 RagContextProvider 实现的行为：
/// Order 15、userMessage 为空返回 null、有 userMessage 时调用 IRagService 并格式化返回。
/// </summary>
public sealed class RagContextProviderTests
{
    private static readonly ILogger<RagContextProvider> NullLog = NullLogger<RagContextProvider>.Instance;
    private static readonly AgentConfig TestAgent = new(
        Id: "agent-rag-test",
        Name: "RAG Test Agent",
        Description: "",
        IsEnabled: true,
        DisabledSkillIds: [],
        DisabledMcpServerIds: [],
        ToolGroupConfigs: [],
        CreatedAtUtc: DateTimeOffset.UtcNow);

    // ── 构造参数校验 ──

    [Fact]
    public void Ctor_NullRagService_Throws()
    {
        var act = () => new RagContextProvider(null!, NullLog);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ragService");
    }

    // ── Order ──

    [Fact]
    public void Order_Is15()
    {
        var sut = new RagContextProvider(Substitute.For<IRagService>(), NullLog);
        sut.Order.Should().Be(15);
    }

    // ── IAgentContextProvider 基础接口（无 userMessage）──

    [Fact]
    public async Task BuildContextAsync_BaseInterface_NoUserMessage_ReturnsNull()
    {
        // 调用基础接口签名（无 userMessage）时应跳过检索
        IAgentContextProvider sut = new RagContextProvider(Substitute.For<IRagService>(), NullLog);
        string? result = await sut.BuildContextAsync(TestAgent, sessionId: null);
        result.Should().BeNull();
    }

    // ── IUserAwareContextProvider（带 userMessage）──

    [Fact]
    public async Task BuildContextAsync_NullUserMessage_ReturnsNull()
    {
        IUserAwareContextProvider sut = new RagContextProvider(Substitute.For<IRagService>(), NullLog);
        string? result = await sut.BuildContextAsync(TestAgent, sessionId: null, userMessage: null);
        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildContextAsync_WhitespaceUserMessage_ReturnsNull()
    {
        IUserAwareContextProvider sut = new RagContextProvider(Substitute.For<IRagService>(), NullLog);
        string? result = await sut.BuildContextAsync(TestAgent, sessionId: null, userMessage: "   ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildContextAsync_EmptyRagResult_ReturnsNull()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.QueryAsync(Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(string.Empty));

        IUserAwareContextProvider sut = new RagContextProvider(ragService, NullLog);
        string? result = await sut.BuildContextAsync(TestAgent, sessionId: null, userMessage: "some query");

        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildContextAsync_WithUserMessage_ReturnsFormattedResult()
    {
        const string ragContent = "relevant document chunk about AI";
        var ragService = Substitute.For<IRagService>();
        ragService.QueryAsync(Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ragContent));

        IUserAwareContextProvider sut = new RagContextProvider(ragService, NullLog);
        string? result = await sut.BuildContextAsync(TestAgent, sessionId: null, userMessage: "what is AI?");

        result.Should().NotBeNull();
        result.Should().Contain("## RAG 相关知识");
        result.Should().Contain(ragContent);
    }

    [Fact]
    public async Task BuildContextAsync_PassesUserMessageToRagService()
    {
        const string userMessage = "explain machine learning";
        var ragService = Substitute.For<IRagService>();
        ragService.QueryAsync(Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("some result"));

        IUserAwareContextProvider sut = new RagContextProvider(ragService, NullLog);
        await sut.BuildContextAsync(TestAgent, sessionId: null, userMessage: userMessage);

        await ragService.Received(1).QueryAsync(
            userMessage, Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildContextAsync_UsesSessionScope()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.QueryAsync(Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("result"));

        IUserAwareContextProvider sut = new RagContextProvider(ragService, NullLog);
        await sut.BuildContextAsync(TestAgent, sessionId: "sess-001", userMessage: "query");

        // 始终使用 Session 作用域（内部自动合并 Global + Session 双库）
        await ragService.Received(1).QueryAsync(
            Arg.Any<string>(), RagScope.Session, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildContextAsync_PassesSessionId_ToRagService()
    {
        const string sessionId = "sess-pass-test";
        var ragService = Substitute.For<IRagService>();
        ragService.QueryAsync(Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("result"));

        IUserAwareContextProvider sut = new RagContextProvider(ragService, NullLog);
        await sut.BuildContextAsync(TestAgent, sessionId: sessionId, userMessage: "query");

        await ragService.Received(1).QueryAsync(
            Arg.Any<string>(), Arg.Any<RagScope>(), sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildContextAsync_ImplementsIAgentContextProvider()
    {
        var provider = new RagContextProvider(Substitute.For<IRagService>(), NullLog);
        provider.Should().BeAssignableTo<IAgentContextProvider>();
        provider.Should().BeAssignableTo<IUserAwareContextProvider>();
    }
}
