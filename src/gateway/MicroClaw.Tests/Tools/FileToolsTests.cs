using FluentAssertions;
using MicroClaw.Infrastructure.Configuration;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tests.Tools;

public sealed class FileToolsTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileToolsOptions _options;
    private readonly IReadOnlyList<AIFunction> _tools;

    public FileToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "microclaw_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);

        _options = new FileToolsOptions();
        _tools = FileTools.Create(_testDir, _options);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { /* best effort */ }
    }

    private AIFunction Tool(string name) => _tools.First(t => t.Name == name);

    private async Task<T> Invoke<T>(string toolName, Dictionary<string, object?> args) where T : class
    {
        object? result = await Tool(toolName).InvokeAsync(new AIFunctionArguments(args));
        // AIFunctionFactory wraps the anonymous-type result; deserialize via JSON round-trip
        string json = System.Text.Json.JsonSerializer.Serialize(result);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
    }

    // ── read_file ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        string path = Path.Combine(_testDir, "hello.txt");
        await File.WriteAllTextAsync(path, "line1\nline2\nline3");

        var result = await Invoke<ReadResult>("read_file", new() { ["filePath"] = path });

        result.success.Should().BeTrue();
        result.content.Should().Contain("line1");
        result.totalLines.Should().Be(3);
    }

    [Fact]
    public async Task ReadFile_WithLineRange_ReturnsSubset()
    {
        string path = Path.Combine(_testDir, "range.txt");
        await File.WriteAllTextAsync(path, "a\nb\nc\nd\ne");

        var result = await Invoke<ReadResult>("read_file", new() { ["filePath"] = path, ["startLine"] = 2, ["endLine"] = 4 });

        result.success.Should().BeTrue();
        result.content.Should().Be("b\nc\nd");
        result.startLine.Should().Be(2);
        result.endLine.Should().Be(4);
    }

    [Fact]
    public async Task ReadFile_LargeFile_Truncated()
    {
        string path = Path.Combine(_testDir, "big.txt");
        var opts = new FileToolsOptions { MaxReadChars = 50 };
        var tools = FileTools.Create(_testDir, opts);
        await File.WriteAllTextAsync(path, new string('A', 200));

        object? result = await tools.First(t => t.Name == "read_file")
            .InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = path }));
        string json = System.Text.Json.JsonSerializer.Serialize(result);

        json.Should().Contain("\"truncated\":true");
    }

    [Fact]
    public async Task ReadFile_PathOutsideAllowed_ReturnsError()
    {
        string outsidePath = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N"), "secret.txt");

        var result = await Invoke<ErrorResult>("read_file", new() { ["filePath"] = outsidePath });

        result.success.Should().BeFalse();
        result.error.Should().Contain("不在允许的目录范围内");
    }

    [Fact]
    public async Task ReadFile_PathTraversal_ReturnsError()
    {
        string traversal = Path.Combine(_testDir, "..", "..", "etc", "passwd");

        var result = await Invoke<ErrorResult>("read_file", new() { ["filePath"] = traversal });

        result.success.Should().BeFalse();
        result.error.Should().Contain("不在允许的目录范围内");
    }

    [Fact]
    public async Task ReadFile_NonexistentFile_ReturnsError()
    {
        string path = Path.Combine(_testDir, "nope.txt");

        var result = await Invoke<ErrorResult>("read_file", new() { ["filePath"] = path });

        result.success.Should().BeFalse();
        result.error.Should().Contain("不存在");
    }

    [Fact]
    public async Task ReadFile_RelativePath_ResolvesToSandbox()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "relative.txt"), "resolved");

        var result = await Invoke<ReadResult>("read_file", new() { ["filePath"] = "relative.txt" });

        result.success.Should().BeTrue();
        result.content.Should().Be("resolved");
    }

    [Fact]
    public async Task WriteFile_RelativePath_CreatesInSandbox()
    {
        var result = await Invoke<WriteResult>("write_file", new() { ["filePath"] = "rel_write.txt", ["content"] = "sandbox content" });

        result.success.Should().BeTrue();
        File.ReadAllText(Path.Combine(_testDir, "rel_write.txt")).Should().Be("sandbox content");
    }

    // ── write_file ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteFile_CreatesNewFile()
    {
        string path = Path.Combine(_testDir, "new.txt");

        var result = await Invoke<WriteResult>("write_file", new() { ["filePath"] = path, ["content"] = "hello world" });

        result.success.Should().BeTrue();
        File.ReadAllText(path).Should().Be("hello world");
    }

    [Fact]
    public async Task WriteFile_OverwritesExisting()
    {
        string path = Path.Combine(_testDir, "overwrite.txt");
        await File.WriteAllTextAsync(path, "old content");

        var result = await Invoke<WriteResult>("write_file", new() { ["filePath"] = path, ["content"] = "new content" });

        result.success.Should().BeTrue();
        File.ReadAllText(path).Should().Be("new content");
    }

    [Fact]
    public async Task WriteFile_CreatesIntermediateDirectories()
    {
        string path = Path.Combine(_testDir, "sub", "deep", "file.txt");

        var result = await Invoke<WriteResult>("write_file", new() { ["filePath"] = path, ["content"] = "nested" });

        result.success.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task WriteFile_PathOutsideAllowed_ReturnsError()
    {
        string outsidePath = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N"), "hack.txt");

        var result = await Invoke<ErrorResult>("write_file", new() { ["filePath"] = outsidePath, ["content"] = "pwned" });

        result.success.Should().BeFalse();
        result.error.Should().Contain("不在允许的目录范围内");
        File.Exists(outsidePath).Should().BeFalse();
    }

    [Fact]
    public async Task WriteFile_ExceedsMaxBytes_ReturnsError()
    {
        var opts = new FileToolsOptions { MaxFileWriteBytes = 10 };
        var tools = FileTools.Create(_testDir, opts);
        string path = Path.Combine(_testDir, "toobig.txt");

        object? result = await tools.First(t => t.Name == "write_file")
            .InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = path, ["content"] = new string('X', 100) }));
        string json = System.Text.Json.JsonSerializer.Serialize(result);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ErrorResult>(json)!;

        deserialized.success.Should().BeFalse();
        deserialized.error.Should().Contain("\u8D85\u51FA\u5355\u6B21\u5199\u5165\u4E0A\u9650");
    }

    // ── edit_file ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditFile_SingleMatch_Replaces()
    {
        string path = Path.Combine(_testDir, "edit.txt");
        await File.WriteAllTextAsync(path, "Hello World\nFoo Bar\nEnd");

        var result = await Invoke<EditResult>("edit_file", new()
        {
            ["filePath"] = path, ["oldText"] = "Foo Bar", ["newText"] = "Baz Qux"
        });

        result.success.Should().BeTrue();
        result.replacements.Should().Be(1);
        File.ReadAllText(path).Should().Contain("Baz Qux");
        File.ReadAllText(path).Should().NotContain("Foo Bar");
    }

    [Fact]
    public async Task EditFile_NoMatch_ReturnsError()
    {
        string path = Path.Combine(_testDir, "edit2.txt");
        await File.WriteAllTextAsync(path, "Hello");

        var result = await Invoke<ErrorResult>("edit_file", new()
        {
            ["filePath"] = path, ["oldText"] = "NotFound", ["newText"] = "X"
        });

        result.success.Should().BeFalse();
        result.error.Should().Contain("未找到");
    }

    [Fact]
    public async Task EditFile_MultipleMatches_ReturnsError()
    {
        string path = Path.Combine(_testDir, "edit3.txt");
        await File.WriteAllTextAsync(path, "AAA BBB AAA");

        var result = await Invoke<ErrorResult>("edit_file", new()
        {
            ["filePath"] = path, ["oldText"] = "AAA", ["newText"] = "CCC"
        });

        result.success.Should().BeFalse();
        result.error.Should().Contain("多处");
    }

    // ── list_directory ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListDirectory_ReturnsFilesAndDirs()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.txt"), "");
        Directory.CreateDirectory(Path.Combine(_testDir, "subdir"));

        var result = await Invoke<ListResult>("list_directory", new() { ["directoryPath"] = "." });

        result.success.Should().BeTrue();
        result.entries.Should().Contain("a.txt");
        result.entries.Should().Contain(e => e.EndsWith("/"));
    }

    [Fact]
    public async Task ListDirectory_Recursive_RespectsDepth()
    {
        // depth 0 → subdir, depth 1 → subdir/deep, depth 2 → subdir/deep/deeper
        Directory.CreateDirectory(Path.Combine(_testDir, "subdir", "deep", "deeper", "too_deep"));
        await File.WriteAllTextAsync(Path.Combine(_testDir, "subdir", "deep", "deeper", "file.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "subdir", "deep", "deeper", "too_deep", "hidden.txt"), "");

        var result = await Invoke<ListResult>("list_directory", new() { ["directoryPath"] = ".", ["recursive"] = true });

        result.success.Should().BeTrue();
        result.entries.Should().Contain("subdir/deep/deeper/file.txt");
        // too_deep/ 目录名会被列出（在 depth 3），但其内部文件不应出现（不递归进入）
        result.entries.Should().Contain(e => e.Contains("too_deep/"));
        result.entries.Should().NotContain(e => e.Contains("hidden.txt"));
    }

    // ── search_files ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchFiles_PlainText_FindsMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "s1.txt"), "hello world\ngoodbye");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "s2.txt"), "nothing here");

        var result = await Invoke<SearchResult>("search_files", new()
        {
            ["directory"] = ".", ["pattern"] = "hello"
        });

        result.success.Should().BeTrue();
        result.totalMatches.Should().Be(1);
    }

    [Fact]
    public async Task SearchFiles_Regex_FindsMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "r1.cs"), "public class Foo {}\nprivate int bar;");

        var result = await Invoke<SearchResult>("search_files", new()
        {
            ["directory"] = ".", ["pattern"] = @"class\s+\w+", ["isRegex"] = true, ["filePattern"] = "*.cs"
        });

        result.success.Should().BeTrue();
        result.totalMatches.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchFiles_PathOutsideAllowed_ReturnsError()
    {
        var result = await Invoke<ErrorResult>("search_files", new()
        {
            ["directory"] = Path.Combine(Path.GetTempPath(), "nonexist_" + Guid.NewGuid().ToString("N")),
            ["pattern"] = "test"
        });

        result.success.Should().BeFalse();
    }

    // ── ValidatePath 直接测试 ────────────────────────────────────────────────

    [Fact]
    public void ValidatePath_EmptyPath_ReturnsError()
    {
        string? result = FileTools.ValidatePath("", [_testDir + Path.DirectorySeparatorChar], mustExist: false);
        result.Should().NotBeNull();
        result.Should().Contain("不能为空");
    }

    [Fact]
    public void ValidatePath_AllowedPath_ReturnsNull()
    {
        string path = Path.Combine(_testDir, "allowed.txt");
        string? result = FileTools.ValidatePath(path, [_testDir + Path.DirectorySeparatorChar], mustExist: false);
        result.Should().BeNull();
    }

    // ── DTO records for deserialization ──────────────────────────────────────

    private sealed record ReadResult(bool success, string content, int startLine, int endLine, int totalLines, bool truncated);
    private sealed record WriteResult(bool success, string filePath, int bytesWritten);
    private sealed record EditResult(bool success, string filePath, int replacements);
    private sealed record ListResult(bool success, string directoryPath, List<string> entries, int totalEntries);
    private sealed record SearchResult(bool success, string directory, string pattern, int totalMatches, int filesScanned, bool limitReached);
    private sealed record ErrorResult(bool success, string error);
}
