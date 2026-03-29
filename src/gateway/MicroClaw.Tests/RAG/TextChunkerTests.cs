using FluentAssertions;
using MicroClaw.RAG;

namespace MicroClaw.Tests.RAG;

public class TextChunkerTests
{
    // ── CountTokens ──

    [Fact]
    public void CountTokens_Empty_Returns_Zero()
    {
        TextChunker.CountTokens("").Should().Be(0);
        TextChunker.CountTokens(null!).Should().Be(0);
    }

    [Fact]
    public void CountTokens_Simple_English()
    {
        // "hello world" ≈ 2 tokens in cl100k_base
        int count = TextChunker.CountTokens("hello world");
        count.Should().BeGreaterThan(0).And.BeLessThan(10);
    }

    [Fact]
    public void CountTokens_Chinese_Text()
    {
        // 中文每个字符通常 1-2 tokens
        int count = TextChunker.CountTokens("你好世界");
        count.Should().BeGreaterThan(0);
    }

    // ── ChunkByTokens — 参数验证 ──

    [Fact]
    public void ChunkByTokens_MaxTokens_Zero_Throws()
    {
        var act = () => TextChunker.ChunkByTokens("text", maxTokens: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChunkByTokens_Overlap_GreaterOrEqual_MaxTokens_Throws()
    {
        var act = () => TextChunker.ChunkByTokens("text", maxTokens: 10, overlapTokens: 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChunkByTokens_Negative_Overlap_Throws()
    {
        var act = () => TextChunker.ChunkByTokens("text", maxTokens: 10, overlapTokens: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── ChunkByTokens — 基本行为 ──

    [Fact]
    public void ChunkByTokens_EmptyText_Returns_Empty()
    {
        TextChunker.ChunkByTokens("").Should().BeEmpty();
        TextChunker.ChunkByTokens("   ").Should().BeEmpty();
    }

    [Fact]
    public void ChunkByTokens_Short_Text_Returns_Single_Chunk()
    {
        var chunks = TextChunker.ChunkByTokens("Hello, world!", maxTokens: 100, overlapTokens: 0);
        chunks.Should().HaveCount(1);
        chunks[0].Index.Should().Be(0);
        chunks[0].Content.Should().Contain("Hello");
        chunks[0].TokenCount.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void ChunkByTokens_Long_Text_Produces_Multiple_Chunks()
    {
        // 生成一个足够长的文本（约 100 tokens = ~75 英文单词）
        string longText = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var chunks = TextChunker.ChunkByTokens(longText, maxTokens: 20, overlapTokens: 0);
        chunks.Should().HaveCountGreaterThan(1);

        // 所有分块 token 数不应超过上限
        chunks.Should().OnlyContain(c => c.TokenCount <= 20);
    }

    [Fact]
    public void ChunkByTokens_Chunks_Have_Sequential_Indices()
    {
        string longText = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var chunks = TextChunker.ChunkByTokens(longText, maxTokens: 20, overlapTokens: 0);

        for (int i = 0; i < chunks.Count; i++)
            chunks[i].Index.Should().Be(i);
    }

    [Fact]
    public void ChunkByTokens_With_Overlap_Produces_Overlapping_Content()
    {
        string longText = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var chunks = TextChunker.ChunkByTokens(longText, maxTokens: 30, overlapTokens: 10);

        chunks.Should().HaveCountGreaterThan(1);

        // 相邻分块应有部分内容重叠
        for (int i = 1; i < chunks.Count; i++)
        {
            // 后一个分块的开头应包含前一个分块末尾的部分内容
            var prevWords = chunks[i - 1].Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currWords = chunks[i].Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // 至少有一些共同的词
            prevWords.Intersect(currWords).Should().NotBeEmpty(
                "相邻分块（{0} 和 {1}）之间应有重叠内容", i - 1, i);
        }
    }

    [Fact]
    public void ChunkByTokens_No_Overlap_Covers_Full_Text()
    {
        string original = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"w{i}"));
        var chunks = TextChunker.ChunkByTokens(original, maxTokens: 10, overlapTokens: 0);

        // 拼接所有分块的内容，应包含原文所有单词
        string joined = string.Join(" ", chunks.Select(c => c.Content));
        foreach (int i in Enumerable.Range(1, 50))
            joined.Should().Contain($"w{i}");
    }

    // ── ChunkByTokens — 默认参数 ──

    [Fact]
    public void ChunkByTokens_Uses_Default_Parameters()
    {
        // 验证默认参数不抛出异常
        var chunks = TextChunker.ChunkByTokens("Short text");
        chunks.Should().HaveCount(1);
    }

    // ── ChunkMarkdown — 参数验证 ──

    [Fact]
    public void ChunkMarkdown_EmptyText_Returns_Empty()
    {
        TextChunker.ChunkMarkdown("").Should().BeEmpty();
        TextChunker.ChunkMarkdown("   ").Should().BeEmpty();
    }

    [Fact]
    public void ChunkMarkdown_Overlap_GreaterOrEqual_MaxTokens_Throws()
    {
        var act = () => TextChunker.ChunkMarkdown("# Title\nBody", maxTokens: 10, overlapTokens: 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── ChunkMarkdown — 基本行为 ──

    [Fact]
    public void ChunkMarkdown_No_Headings_Returns_Single_Chunk()
    {
        string text = "This is a plain text paragraph without any headings.";
        var chunks = TextChunker.ChunkMarkdown(text, maxTokens: 512);
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("plain text");
    }

    [Fact]
    public void ChunkMarkdown_Splits_By_Headings()
    {
        string md = """
            # Introduction
            This is the intro section.

            # Methods
            This is the methods section.

            # Results
            This is the results section.
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);

        chunks.Should().HaveCountGreaterThanOrEqualTo(3);
        chunks.Should().Contain(c => c.Content.Contains("Introduction") || c.Content.Contains("intro"));
        chunks.Should().Contain(c => c.Content.Contains("Methods") || c.Content.Contains("methods"));
        chunks.Should().Contain(c => c.Content.Contains("Results") || c.Content.Contains("results"));
    }

    [Fact]
    public void ChunkMarkdown_Nested_Headings()
    {
        string md = """
            # Chapter 1
            Intro text.

            ## Section 1.1
            Section body.

            ## Section 1.2
            Another section.

            # Chapter 2
            Second chapter text.
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);

        chunks.Should().HaveCountGreaterThanOrEqualTo(4);
        chunks.Should().Contain(c => c.Content.Contains("Section 1.1"));
        chunks.Should().Contain(c => c.Content.Contains("Chapter 2"));
    }

    [Fact]
    public void ChunkMarkdown_Long_Section_Degrades_To_Token_Chunking()
    {
        // 创建一个标题下有大量内容的 Markdown
        string longBody = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Sentence number {i} with some additional text for length."));
        string md = $"# Very Long Section\n{longBody}";

        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 50, overlapTokens: 10);

        chunks.Should().HaveCountGreaterThan(1);
        // 所有降级分块应带有标题前缀
        chunks.Should().OnlyContain(c => c.Content.Contains("Very Long Section"));
    }

    [Fact]
    public void ChunkMarkdown_Preserves_Heading_Context_In_SubChunks()
    {
        string longBody = string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}"));
        string md = $"# Main Title\n## Sub Section\n{longBody}";

        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 50, overlapTokens: 10);

        // 降级分块中应保留标题链
        foreach (var chunk in chunks.Where(c => c.Content.Contains("word")))
        {
            chunk.Content.Should().Contain("Sub Section",
                "降级分块应保留 Markdown 标题上下文");
        }
    }

    [Fact]
    public void ChunkMarkdown_Preamble_Before_First_Heading()
    {
        string md = """
            Some text before any heading.

            # First Heading
            First section body.
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);

        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
        chunks[0].Content.Should().Contain("Some text before");
    }

    [Fact]
    public void ChunkMarkdown_Sequential_Indices()
    {
        string md = """
            # A
            Text A.
            # B
            Text B.
            # C
            Text C.
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);

        for (int i = 0; i < chunks.Count; i++)
            chunks[i].Index.Should().Be(i);
    }

    // ── 边界场景 ──

    [Fact]
    public void ChunkByTokens_SingleToken_MaxTokens()
    {
        // maxTokens=1 应该每个 token 一个分块
        var chunks = TextChunker.ChunkByTokens("hello world foo bar", maxTokens: 1, overlapTokens: 0);
        chunks.Should().HaveCountGreaterThanOrEqualTo(4);
        chunks.Should().OnlyContain(c => c.TokenCount == 1);
    }

    [Fact]
    public void ChunkMarkdown_Only_Headings_No_Body()
    {
        string md = """
            # Title 1
            # Title 2
            # Title 3
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);
        // 每个标题是一个段落
        chunks.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ChunkMarkdown_Mixed_Heading_Levels()
    {
        string md = """
            # H1
            Content 1.
            ### H3 skipping H2
            Content 3.
            ###### H6
            Content 6.
            """;
        var chunks = TextChunker.ChunkMarkdown(md, maxTokens: 500);
        chunks.Should().HaveCountGreaterThanOrEqualTo(3);
    }
}
