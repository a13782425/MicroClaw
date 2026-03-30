using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace MicroClaw.RAG;

/// <summary>
/// 文本分块器：支持固定长度滑动窗口分块和 Markdown 标题感知分块。
/// 使用 cl100k_base 编码（OpenAI text-embedding-3-small 等模型使用的编码方式）进行精确 token 计数。
/// </summary>
public static partial class TextChunker
{
    /// <summary>默认分块大小（token 数）。</summary>
    public const int DefaultMaxTokens = 512;

    /// <summary>默认重叠窗口大小（token 数）。</summary>
    public const int DefaultOverlapTokens = 128;

    private static readonly Tokenizer s_tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    /// <summary>
    /// 计算文本的 token 数量。
    /// </summary>
    public static int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return s_tokenizer.CountTokens(text);
    }

    /// <summary>
    /// 固定长度 + 重叠窗口分块。
    /// </summary>
    /// <param name="text">待分块的文本。</param>
    /// <param name="maxTokens">每个分块的最大 token 数。</param>
    /// <param name="overlapTokens">相邻分块间的重叠 token 数。</param>
    /// <returns>分块结果列表。</returns>
    public static List<TextChunk> ChunkByTokens(string text, int maxTokens = DefaultMaxTokens, int overlapTokens = DefaultOverlapTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        if (overlapTokens >= maxTokens)
            throw new ArgumentOutOfRangeException(nameof(overlapTokens), "重叠 token 数必须小于分块大小");

        var result = new List<TextChunk>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var allTokenIds = s_tokenizer.EncodeToIds(text);
        int totalTokens = allTokenIds.Count;
        if (totalTokens == 0) return result;

        int step = maxTokens - overlapTokens;
        int chunkIndex = 0;

        for (int start = 0; start < totalTokens; start += step)
        {
            int end = Math.Min(start + maxTokens, totalTokens);
            int count = end - start;

            // 使用 token range 解码回文本
            string chunkText = s_tokenizer.Decode(allTokenIds.Skip(start).Take(count))!;
            result.Add(new TextChunk(chunkIndex++, chunkText.Trim(), count));

            // 最后一个分块到末尾就停止
            if (end >= totalTokens) break;
        }

        return result;
    }

    /// <summary>
    /// Markdown 标题感知分块：先按 <c>#</c> 标题层级拆分为语义段落，
    /// 段落超过 <paramref name="maxTokens"/> 时降级为固定长度分块。
    /// 每个分块的标题上下文（祖先标题链）会作为前缀注入，确保语义完整性。
    /// </summary>
    /// <param name="markdown">Markdown 文本。</param>
    /// <param name="maxTokens">每个分块的最大 token 数。</param>
    /// <param name="overlapTokens">固定长度降级分块时的重叠 token 数。</param>
    /// <returns>分块结果列表。</returns>
    public static List<TextChunk> ChunkMarkdown(string markdown, int maxTokens = DefaultMaxTokens, int overlapTokens = DefaultOverlapTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        if (overlapTokens >= maxTokens)
            throw new ArgumentOutOfRangeException(nameof(overlapTokens), "重叠 token 数必须小于分块大小");

        var result = new List<TextChunk>();
        if (string.IsNullOrWhiteSpace(markdown)) return result;

        var sections = SplitByHeadings(markdown);
        int chunkIndex = 0;

        foreach (var section in sections)
        {
            string sectionText = section.Content;
            if (string.IsNullOrWhiteSpace(sectionText)) continue;

            // 构建标题前缀（祖先标题链）
            string prefix = section.HeadingPrefix;

            int sectionTokens = CountTokens(sectionText);
            if (sectionTokens <= maxTokens)
            {
                // 整段放入一个分块
                result.Add(new TextChunk(chunkIndex++, sectionText.Trim(), sectionTokens));
            }
            else
            {
                // 段落超长，降级为固定长度分块，每块带标题前缀
                string bodyText = section.BodyWithoutHeading;
                int prefixTokens = CountTokens(prefix);
                int bodyMaxTokens = Math.Max(maxTokens - prefixTokens, maxTokens / 2);

                var subChunks = ChunkByTokens(bodyText, bodyMaxTokens, overlapTokens);
                foreach (var sub in subChunks)
                {
                    string combined = string.IsNullOrEmpty(prefix)
                        ? sub.Content
                        : prefix + "\n" + sub.Content;
                    int combinedTokens = CountTokens(combined);
                    result.Add(new TextChunk(chunkIndex++, combined.Trim(), combinedTokens));
                }
            }
        }

        return result;
    }

    // ── Markdown 标题拆分 ──

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    /// <summary>
    /// 按 Markdown 标题行拆分文本为语义段落。
    /// </summary>
    private static List<MarkdownSection> SplitByHeadings(string markdown)
    {
        var sections = new List<MarkdownSection>();
        var headingStack = new List<string>(); // headingStack[i] = level i+1 的标题文本
        var matches = HeadingRegex().Matches(markdown);

        if (matches.Count == 0)
        {
            // 无标题，整篇作为单个段落
            sections.Add(new MarkdownSection("", "", markdown));
            return sections;
        }

        // 标题前的内容（如果有）
        int firstHeadingPos = matches[0].Index;
        if (firstHeadingPos > 0)
        {
            string preamble = markdown[..firstHeadingPos];
            if (!string.IsNullOrWhiteSpace(preamble))
                sections.Add(new MarkdownSection("", "", preamble));
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int level = match.Groups[1].Value.Length; // # 数量
            string headingText = match.Groups[2].Value.Trim();
            string headingLine = match.Value;

            // 更新标题栈
            // 截断到当前层级及以上
            while (headingStack.Count >= level)
                headingStack.RemoveAt(headingStack.Count - 1);
            // 填充跳过的层级
            while (headingStack.Count < level - 1)
                headingStack.Add("");
            headingStack.Add(headingText);

            // 构建标题前缀（祖先标题链）
            var prefixBuilder = new StringBuilder();
            for (int l = 0; l < headingStack.Count; l++)
            {
                if (!string.IsNullOrEmpty(headingStack[l]))
                {
                    prefixBuilder.Append(new string('#', l + 1));
                    prefixBuilder.Append(' ');
                    prefixBuilder.AppendLine(headingStack[l]);
                }
            }
            string headingPrefix = prefixBuilder.ToString().TrimEnd();

            // 取该标题到下一个标题之间的内容
            int contentStart = match.Index;
            int contentEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : markdown.Length;
            string sectionContent = markdown[contentStart..contentEnd];

            // body 不含标题行本身
            int bodyStart = match.Index + match.Length;
            string body = markdown[bodyStart..contentEnd];

            sections.Add(new MarkdownSection(headingPrefix, body, sectionContent));
        }

        return sections;
    }

    /// <summary>Markdown 段落（内部使用）。</summary>
    private sealed record MarkdownSection(string HeadingPrefix, string BodyWithoutHeading, string Content);
}
