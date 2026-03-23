using FluentAssertions;
using MicroClaw.Agent.Memory;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

public sealed class SessionDnaServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _dir = new();
    private readonly SessionDnaService _svc;

    public SessionDnaServiceTests()
    {
        _svc = new SessionDnaService(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    // ── FixedFileNames ────────────────────────────────────────────────────────

    [Fact]
    public void FixedFileNames_Contains_ThreeExpectedFiles()
    {
        SessionDnaService.FixedFileNames.Should().BeEquivalentTo(
            ["SOUL.md", "USER.md", "AGENTS.md"],
            opts => opts.WithStrictOrdering());
    }

    // ── IsAllowedFileName ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("SOUL.md")]
    [InlineData("USER.md")]
    [InlineData("AGENTS.md")]
    public void IsAllowedFileName_ValidNames_ReturnsTrue(string fileName)
    {
        SessionDnaService.IsAllowedFileName(fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData("soul.md")]          // 大小写不同
    [InlineData("SOUL")]             // 无扩展名
    [InlineData("hack.md")]          // 非法文件名
    [InlineData("../SOUL.md")]       // 路径穿越
    [InlineData("")]
    [InlineData("MEMORY.md")]
    public void IsAllowedFileName_InvalidNames_ReturnsFalse(string fileName)
    {
        SessionDnaService.IsAllowedFileName(fileName).Should().BeFalse();
    }

    // ── InitializeSession ─────────────────────────────────────────────────────

    [Fact]
    public void InitializeSession_CreatesThreeFiles()
    {
        _svc.InitializeSession("sess1");

        foreach (string fileName in SessionDnaService.FixedFileNames)
        {
            string path = Path.Combine(_dir.Path, "sess1", fileName);
            File.Exists(path).Should().BeTrue($"{fileName} 应当存在");
        }
    }

    [Fact]
    public void InitializeSession_FilesContainTemplateContent()
    {
        _svc.InitializeSession("sess1");

        string soul = File.ReadAllText(Path.Combine(_dir.Path, "sess1", "SOUL.md"));
        string user = File.ReadAllText(Path.Combine(_dir.Path, "sess1", "USER.md"));
        string agents = File.ReadAllText(Path.Combine(_dir.Path, "sess1", "AGENTS.md"));

        soul.Should().Contain("Soul").And.NotBeNullOrWhiteSpace();
        user.Should().Contain("User").And.NotBeNullOrWhiteSpace();
        agents.Should().Contain("Agents").And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InitializeSession_IsIdempotent_DoesNotOverwriteExisting()
    {
        _svc.InitializeSession("sess1");

        // 修改文件内容
        const string customContent = "# Custom soul content";
        File.WriteAllText(Path.Combine(_dir.Path, "sess1", "SOUL.md"), customContent);

        // 再次初始化，不应覆盖
        _svc.InitializeSession("sess1");

        string content = File.ReadAllText(Path.Combine(_dir.Path, "sess1", "SOUL.md"));
        content.Should().Be(customContent);
    }

    // ── ListFiles ─────────────────────────────────────────────────────────────

    [Fact]
    public void ListFiles_NoFiles_ReturnsThreeEntriesWithEmptyContent()
    {
        var files = _svc.ListFiles("sess-no-files");

        files.Should().HaveCount(3);
        files.Select(f => f.FileName).Should().BeEquivalentTo(
            ["SOUL.md", "USER.md", "AGENTS.md"],
            opts => opts.WithStrictOrdering());
        files.Should().AllSatisfy(f => f.Content.Should().BeEmpty());
    }

    [Fact]
    public void ListFiles_AfterInit_ReturnsThreeFilesWithContent()
    {
        _svc.InitializeSession("sess1");

        var files = _svc.ListFiles("sess1");

        files.Should().HaveCount(3);
        files.Should().AllSatisfy(f => f.Content.Should().NotBeNullOrWhiteSpace());
        files.Should().AllSatisfy(f => f.Description.Should().NotBeNullOrWhiteSpace());
        files.Should().AllSatisfy(f => f.UpdatedAt.Should().BeAfter(DateTimeOffset.MinValue));
    }

    [Fact]
    public void ListFiles_ReturnsDescriptions()
    {
        var files = _svc.ListFiles("sess1");

        files[0].Description.Should().Be("定义 AI 的人格、语气和表达风格");
        files[1].Description.Should().Be("定义对话对象的画像、偏好和背景信息");
        files[2].Description.Should().Be("定义工作流、决策规则和处理步骤");
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Read_ValidFileName_AfterInit_ReturnsContent()
    {
        _svc.InitializeSession("sess1");

        var file = _svc.Read("sess1", "SOUL.md");

        file.Should().NotBeNull();
        file!.FileName.Should().Be("SOUL.md");
        file.Content.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Read_InvalidFileName_ReturnsNull()
    {
        var result = _svc.Read("sess1", "hack.md");

        result.Should().BeNull();
    }

    [Fact]
    public void Read_FileNotYetCreated_ReturnsEmptyContent()
    {
        var result = _svc.Read("sess-new", "SOUL.md");

        result.Should().NotBeNull();
        result!.Content.Should().BeEmpty();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ValidFileName_PersistsContent()
    {
        const string newContent = "# Updated soul\nHello world";

        var result = _svc.Update("sess1", "SOUL.md", newContent);

        result.Should().NotBeNull();
        result!.FileName.Should().Be("SOUL.md");
        result.Content.Should().Be(newContent);

        // 验证磁盘上确实写入
        string path = Path.Combine(_dir.Path, "sess1", "SOUL.md");
        File.ReadAllText(path).Should().Be(newContent);
    }

    [Fact]
    public void Update_InvalidFileName_ReturnsNull()
    {
        var result = _svc.Update("sess1", "MEMORY.md", "content");

        result.Should().BeNull();
    }

    [Fact]
    public void Update_CreatesDirectoryIfMissing()
    {
        // "sess-fresh" 目录不存在
        _svc.Update("sess-fresh", "USER.md", "content");

        string path = Path.Combine(_dir.Path, "sess-fresh", "USER.md");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Update_WithEmptyContent_WritesEmptyFile()
    {
        var result = _svc.Update("sess1", "AGENTS.md", string.Empty);

        result.Should().NotBeNull();
        result!.Content.Should().BeEmpty();
    }

    // ── BuildDnaContext ───────────────────────────────────────────────────────

    [Fact]
    public void BuildDnaContext_NoFiles_ReturnsEmptyString()
    {
        string ctx = _svc.BuildDnaContext("sess-empty");

        ctx.Should().BeEmpty();
    }

    [Fact]
    public void BuildDnaContext_AfterInit_ContainsAllThreeFiles()
    {
        _svc.InitializeSession("sess1");

        string ctx = _svc.BuildDnaContext("sess1");

        ctx.Should().Contain("Soul");
        ctx.Should().Contain("User");
        ctx.Should().Contain("Agents");
    }

    [Fact]
    public void BuildDnaContext_SkipsEmptyFiles()
    {
        _svc.Update("sess1", "SOUL.md", "Soul content");
        _svc.Update("sess1", "USER.md", "   ");   // 纯空白，应跳过
        _svc.Update("sess1", "AGENTS.md", "Agents content");

        string ctx = _svc.BuildDnaContext("sess1");

        ctx.Should().Contain("Soul content");
        ctx.Should().NotContain("   ");
        ctx.Should().Contain("Agents content");
    }

    [Fact]
    public void BuildDnaContext_JoinsWithDoubleNewline()
    {
        _svc.Update("sess1", "SOUL.md", "SoulContent");
        _svc.Update("sess1", "USER.md", "UserContent");
        _svc.Update("sess1", "AGENTS.md", "AgentsContent");

        string ctx = _svc.BuildDnaContext("sess1");

        // 三段之间用双换行分隔
        ctx.Should().Contain("SoulContent\n\nUserContent\n\nAgentsContent");
    }

    // ── DeleteSessionDnaFiles ─────────────────────────────────────────────────

    [Fact]
    public void DeleteSessionDnaFiles_AfterInit_RemovesAllThreeFiles()
    {
        _svc.InitializeSession("sess1");

        _svc.DeleteSessionDnaFiles("sess1");

        foreach (string fileName in SessionDnaService.FixedFileNames)
        {
            string path = Path.Combine(_dir.Path, "sess1", fileName);
            File.Exists(path).Should().BeFalse($"{fileName} 应已被删除");
        }
    }

    [Fact]
    public void DeleteSessionDnaFiles_NonExistentSession_DoesNotThrow()
    {
        _svc.Invoking(s => s.DeleteSessionDnaFiles("non-existent"))
            .Should().NotThrow();
    }
}
