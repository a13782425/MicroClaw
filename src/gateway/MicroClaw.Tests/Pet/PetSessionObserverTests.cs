using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Storage;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetSessionObserver 单元测试：
/// - ObserveMessageAsync：习惯记录写入 habits.jsonl
/// - 异常静默处理
/// </summary>
public sealed class PetSessionObserverTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private const string SessionId = "observer-test-session";

    public void Dispose() => _tempDir.Dispose();

    private PetSessionObserver CreateObserver() =>
        new(_tempDir.Path, NullLogger<PetSessionObserver>.Instance);

    [Fact]
    public async Task ObserveMessageAsync_WritesHabitEntry()
    {
        // Arrange
        var observer = CreateObserver();
        string petDir = Path.Combine(_tempDir.Path, SessionId, "pet");
        Directory.CreateDirectory(petDir);

        var dispatch = new PetDispatchResult
        {
            AgentId = "agent-code",
            ProviderId = "gpt4",
            ShouldPetRespond = false,
            Reason = "用户需要编程帮助",
        };

        // Act
        await observer.ObserveMessageAsync(SessionId, dispatch, succeeded: true);

        // Assert
        string habitsFile = Path.Combine(petDir, "habits.jsonl");
        File.Exists(habitsFile).Should().BeTrue();
        string[] lines = await File.ReadAllLinesAsync(habitsFile);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("agent-code");
        lines[0].Should().Contain("gpt4");
        lines[0].Should().Contain("\"Succeeded\":true");
    }

    [Fact]
    public async Task ObserveMessageAsync_MultipleEntries_AppendsToFile()
    {
        // Arrange
        var observer = CreateObserver();
        string petDir = Path.Combine(_tempDir.Path, SessionId, "pet");
        Directory.CreateDirectory(petDir);

        var dispatch1 = new PetDispatchResult { AgentId = "a1", Reason = "r1" };
        var dispatch2 = new PetDispatchResult { AgentId = "a2", Reason = "r2" };

        // Act
        await observer.ObserveMessageAsync(SessionId, dispatch1, succeeded: true);
        await observer.ObserveMessageAsync(SessionId, dispatch2, succeeded: false);

        // Assert
        string habitsFile = Path.Combine(petDir, "habits.jsonl");
        string[] lines = await File.ReadAllLinesAsync(habitsFile);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("a1");
        lines[1].Should().Contain("a2");
        lines[1].Should().Contain("\"Succeeded\":false");
    }

    [Fact]
    public async Task ObserveMessageAsync_PetResponded_RecordsCorrectly()
    {
        // Arrange
        var observer = CreateObserver();
        string petDir = Path.Combine(_tempDir.Path, SessionId, "pet");
        Directory.CreateDirectory(petDir);

        var dispatch = new PetDispatchResult
        {
            ShouldPetRespond = true,
            PetResponse = "我现在心情不错！",
            Reason = "用户询问 Pet 状态",
        };

        // Act
        await observer.ObserveMessageAsync(SessionId, dispatch, succeeded: true);

        // Assert
        string habitsFile = Path.Combine(petDir, "habits.jsonl");
        string content = await File.ReadAllTextAsync(habitsFile);
        content.Should().Contain("\"PetResponded\":true");
    }

    [Fact]
    public async Task ObserveMessageAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var observer = CreateObserver();
        // 不预先创建目录

        var dispatch = new PetDispatchResult { Reason = "test" };

        // Act
        await observer.ObserveMessageAsync(SessionId, dispatch, succeeded: true);

        // Assert
        string habitsFile = Path.Combine(_tempDir.Path, SessionId, "pet", "habits.jsonl");
        File.Exists(habitsFile).Should().BeTrue();
    }

    [Fact]
    public async Task ObserveMessageAsync_ToolOverrides_RecordsCount()
    {
        // Arrange
        var observer = CreateObserver();
        string petDir = Path.Combine(_tempDir.Path, SessionId, "pet");
        Directory.CreateDirectory(petDir);

        var dispatch = new PetDispatchResult
        {
            ToolOverrides = [
                new MicroClaw.Tools.ToolGroupConfig("fetch", true, []),
                new MicroClaw.Tools.ToolGroupConfig("shell", false, []),
            ],
            PetKnowledge = null,
            Reason = "test",
        };

        // Act
        await observer.ObserveMessageAsync(SessionId, dispatch, succeeded: true);

        // Assert
        string habitsFile = Path.Combine(petDir, "habits.jsonl");
        string content = await File.ReadAllTextAsync(habitsFile);
        content.Should().Contain("\"ToolOverrideCount\":2");
    }
}
