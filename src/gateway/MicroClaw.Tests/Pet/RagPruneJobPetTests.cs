using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.Jobs;
using MicroClaw.Pet.Rag;
using MicroClaw.RAG;
using MicroClaw.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// RagPruneJob Pet RAG 扩展测试：
/// - 有 PetRagScope 时扫描 Pet RAG 库并调用 PruneIfNeededAsync
/// - 无 PetRagScope（null）时正常跳过 Pet 清理
/// </summary>
public sealed class RagPruneJobPetTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_PrunesPetRagLibraries()
    {
        // Use real RagDbContextFactory and PetRagScope in a temp directory
        var dbFactory = new RagDbContextFactory(_tempDir.Path);
        var embedding = CreateMockEmbeddingService();
        var sessionsDir = Path.Combine(_tempDir.Path, "sessions");
        var petRagScope = new PetRagScope(embedding, sessionsDir, NullLogger<PetRagScope>.Instance);
        var pruner = Substitute.For<IRagPruner>();

        // Create two pet knowledge.db files by ingesting some content
        await petRagScope.IngestAsync("test content 1", "session-1");
        await petRagScope.IngestAsync("test content 2", "session-2");

        var job = new RagPruneJob(pruner, dbFactory, petRagScope, NullLogger<RagPruneJob>.Instance);

        // Should not throw; will scan and find 2 pet sessions
        await job.ExecuteAsync(CancellationToken.None);

        // Verify pruner was called for global scope
        await pruner.Received(1).PruneIfNeededAsync(RagScope.Global, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoPetRagScope_SkipsPetPruning()
    {
        var dbFactory = new RagDbContextFactory(_tempDir.Path);
        var pruner = Substitute.For<IRagPruner>();

        var job = new RagPruneJob(pruner, dbFactory, null, NullLogger<RagPruneJob>.Instance);

        // Should not throw
        await job.ExecuteAsync(CancellationToken.None);
    }

    private static IEmbeddingService CreateMockEmbeddingService()
    {
        var mock = Substitute.For<IEmbeddingService>();
        mock.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ReadOnlyMemory<float>(new float[128]));
        mock.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.ArgAt<IEnumerable<string>>(0).ToList();
                IReadOnlyList<ReadOnlyMemory<float>> result = texts
                    .Select(_ => new ReadOnlyMemory<float>(new float[128]))
                    .ToList();
                return result;
            });
        return mock;
    }
}
