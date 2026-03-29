using FluentAssertions;
using MicroClaw.Agent.Sessions;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.RAG;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证 <see cref="SessionMessageIndexer"/> 的增量索引逻辑。
/// </summary>
public sealed class SessionMessageIndexerTests
{
    private static SessionMessage MakeMessage(
        string id, string role, string content,
        string? visibility = null) =>
        new(Id: id, Role: role, Content: content,
            ThinkContent: null, Timestamp: DateTimeOffset.UtcNow,
            Attachments: null, Visibility: visibility);

    // ── 构造参数校验 ──

    [Fact]
    public void Ctor_NullRagService_Throws()
    {
        var act = () => new SessionMessageIndexer(null!, NullLogger<SessionMessageIndexer>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ragService");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new SessionMessageIndexer(Substitute.For<IRagService>(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── sessionId 校验 ──

    [Fact]
    public async Task IndexNewMessages_EmptySessionId_Throws()
    {
        var sut = new SessionMessageIndexer(Substitute.For<IRagService>(), NullLogger<SessionMessageIndexer>.Instance);
        var act = async () => await sut.IndexNewMessagesAsync("  ", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── 过滤逻辑 ──

    [Fact]
    public async Task IndexNewMessages_NoMessages_DoesNotCallIngest()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        await sut.IndexNewMessagesAsync("sess-001", []);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexNewMessages_OnlyToolMessages_NothingIndexed()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("t1", "tool", "tool_result_data"),
            MakeMessage("t2", "system", "system prompt"),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexNewMessages_InternalMessages_Excluded()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("u1", "user", "hello", MessageVisibility.Internal),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexNewMessages_EmptyContent_Excluded()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("u1", "user", "   "),
            MakeMessage("a1", "assistant", ""),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── 增量索引逻辑 ──

    [Fact]
    public async Task IndexNewMessages_AlreadyIndexed_Skipped()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "msg:u1" }));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("u1", "user", "already indexed message"),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexNewMessages_NewMessages_IngestCalledWithCorrectSourceId()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("msg001", "user", "hello"),
            MakeMessage("msg002", "assistant", "hi there"),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.Received(1)
            .IngestAsync("user: hello", "msg:msg001", RagScope.Session, "sess-001", Arg.Any<CancellationToken>());
        await ragService.Received(1)
            .IngestAsync("assistant: hi there", "msg:msg002", RagScope.Session, "sess-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexNewMessages_MixOfNewAndIndexed_OnlyNewIndexed()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "msg:old01" }));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[]
        {
            MakeMessage("old01", "user", "old message"),
            MakeMessage("new02", "assistant", "new response"),
        };

        await sut.IndexNewMessagesAsync("sess-001", messages);

        await ragService.DidNotReceive()
            .IngestAsync(Arg.Any<string>(), "msg:old01", Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await ragService.Received(1)
            .IngestAsync("assistant: new response", "msg:new02", RagScope.Session, "sess-001", Arg.Any<CancellationToken>());
    }

    // ── 错误处理 ──

    [Fact]
    public async Task IndexNewMessages_IngestThrows_DoesNotPropagate()
    {
        var ragService = Substitute.For<IRagService>();
        ragService.GetIndexedSourceIdsAsync(Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));
        ragService.IngestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RagScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("DB error")));

        var sut = new SessionMessageIndexer(ragService, NullLogger<SessionMessageIndexer>.Instance);
        var messages = new[] { MakeMessage("u1", "user", "hello") };

        // 异常应被 catch 住，不向上传播
        var act = async () => await sut.IndexNewMessagesAsync("sess-001", messages);
        await act.Should().NotThrowAsync();
    }
}
