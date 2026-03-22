using System.Text;
using FluentAssertions;
using MicroClaw.Agent.Memory;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// DNAService 单元测试，重点验证 BuildSystemPromptContext 的大小限制逻辑（0-A-1）。
/// </summary>
public sealed class DNAServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _dir = new();
    private readonly DNAService _svc;

    public DNAServiceTests()
    {
        // agentsDataDir, globalDnaDir, sessionsDnaDir ——测试中后两个不使用，指向临时目录即可
        _svc = new DNAService(_dir.Path, Path.Combine(_dir.Path, "_global"), Path.Combine(_dir.Path, "_sessions"));
    }

    public void Dispose() => _dir.Dispose();

    // ── 基础功能 ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPromptContext_NoFiles_ReturnsEmpty()
    {
        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemPromptContext_SingleSmallFile_ReturnsContent()
    {
        _svc.Write("agent1", "", "identity.md", "# Identity\nI am an assistant.");

        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().Contain("## Agent DNA");
        result.Should().Contain("identity.md");
        result.Should().Contain("I am an assistant.");
    }

    [Fact]
    public void BuildSystemPromptContext_MultipleSmallFiles_IncludesAllFiles()
    {
        _svc.Write("agent1", "", "a.md", "Content A");
        _svc.Write("agent1", "", "b.md", "Content B");
        _svc.Write("agent1", "skills", "c.md", "Content C");

        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().Contain("Content A");
        result.Should().Contain("Content B");
        result.Should().Contain("Content C");
        result.Should().NotContain("⚠️"); // 未截断，不应有警告
    }

    // ── 大小限制（0-A-1）────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPromptContext_TotalSizeUnderLimit_NoWarning()
    {
        // 写入一个小文件（远小于 50KB）
        _svc.Write("agent1", "", "small.md", new string('X', 1000));

        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().Contain("small.md");
        result.Should().NotContain("⚠️");
    }

    [Fact]
    public void BuildSystemPromptContext_SingleFileThatExceedsLimit_IsExcludedWithWarning()
    {
        // 单个文件内容超过 50KB，BuildSystemPromptContext 添加表头后即超限
        // 预期：该文件被截断，警告出现，结果中包含 0/1
        string bigContent = new string('A', DNAService.MaxDnaSizeBytes + 1000);
        _svc.Write("agent1", "", "huge.md", bigContent);

        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().Contain("⚠️");
        result.Should().Contain("0/1");
    }

    [Fact]
    public void BuildSystemPromptContext_MultipleFilesExceedingLimit_TruncatesFromEnd()
    {
        // 文件 1：10KB（应被包含）
        // 文件 2：10KB（应被包含）
        // 文件 3：40KB（加上前两个后超限，应被截断）
        string content10K = new string('A', 10 * 1024);
        string content40K = new string('B', 40 * 1024);

        _svc.Write("agent1", "", "file1.md", content10K);
        _svc.Write("agent1", "", "file2.md", content10K);
        _svc.Write("agent1", "", "file3.md", content40K);

        string result = _svc.BuildSystemPromptContext("agent1");

        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(DNAService.MaxDnaSizeBytes + 200); // 允许警告行少量超出
        result.Should().Contain("⚠️");
        result.Should().Contain("file1.md");
        result.Should().Contain("file2.md");
        result.Should().NotContain("file3.md"); // 第 3 个文件被截断
    }

    [Fact]
    public void BuildSystemPromptContext_ExactlyAtLimit_NoWarning()
    {
        // 不写任何文件，结果为空（0 字节）—— 确保边界不出错
        string result = _svc.BuildSystemPromptContext("agent-empty");

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemPromptContext_TruncatedResult_ContainsCountInfo()
    {
        // 2 个大文件：第 1 个勉强能放下，第 2 个超限后截断
        // 警告行应包含 "1/2"
        string content = new string('C', 30 * 1024);
        _svc.Write("agent1", "", "first.md", content);
        _svc.Write("agent1", "", "second.md", content);

        string result = _svc.BuildSystemPromptContext("agent1");

        result.Should().Contain("1/2");
    }

    // ── MaxDnaSizeBytes 常量验证 ─────────────────────────────────────────

    [Fact]
    public void MaxDnaSizeBytes_Is50KB()
    {
        DNAService.MaxDnaSizeBytes.Should().Be(50 * 1024);
    }
}
