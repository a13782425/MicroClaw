using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels.Feishu;

namespace MicroClaw.Tests.Channels;

/// <summary>
/// F-H-3: 单元测试 — FeishuMessageProcessor 卡片渲染转换（F-A-5）。
/// 覆盖：ContainsMarkdown 各 Markdown 特征检测 + BuildCardJson 输出结构正确性。
/// </summary>
public sealed class FeishuCardRenderTests
{
    // ══════════════════════════════════════════════════════════════════════
    // ContainsMarkdown — 负例（应返回 false）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContainsMarkdown_Empty_ReturnsFalse()
    {
        FeishuMessageProcessor.ContainsMarkdown("")
            .Should().BeFalse();
    }

    [Fact]
    public void ContainsMarkdown_Whitespace_ReturnsFalse()
    {
        FeishuMessageProcessor.ContainsMarkdown("   \t\n  ")
            .Should().BeFalse();
    }

    [Fact]
    public void ContainsMarkdown_PlainText_ReturnsFalse()
    {
        FeishuMessageProcessor.ContainsMarkdown("这是一段普通文字，没有任何 Markdown 格式。")
            .Should().BeFalse();
    }

    [Fact]
    public void ContainsMarkdown_InlineCode_ReturnsFalse()
    {
        // 单个反引号内联代码不触发（规则仅检测三个反引号的围栏代码块）
        FeishuMessageProcessor.ContainsMarkdown("请调用 `foo()` 函数")
            .Should().BeFalse();
    }

    [Fact]
    public void ContainsMarkdown_PoundSignWithoutSpace_ReturnsFalse()
    {
        // ##标题（无空格）不是合法 Markdown 标题语法，规则要求 ^ #{1,6}\s
        FeishuMessageProcessor.ContainsMarkdown("##这不是标题")
            .Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    // ContainsMarkdown — 正例（应返回 true）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContainsMarkdown_CodeBlock_ReturnsTrue()
    {
        string text = "请看代码：\n```csharp\nConsole.WriteLine(\"Hello\");\n```";
        FeishuMessageProcessor.ContainsMarkdown(text)
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_MarkdownTable_ReturnsTrue()
    {
        string text = "| 姓名 | 分数 |\n|------|------|\n| 张三 | 90   |";
        FeishuMessageProcessor.ContainsMarkdown(text)
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_H1Heading_ReturnsTrue()
    {
        FeishuMessageProcessor.ContainsMarkdown("# 一级标题\n\n正文内容")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_H6Heading_ReturnsTrue()
    {
        FeishuMessageProcessor.ContainsMarkdown("###### 六级标题")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_UnorderedListDash_ReturnsTrue()
    {
        FeishuMessageProcessor.ContainsMarkdown("优点：\n- 性能好\n- 易维护")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_UnorderedListAsterisk_ReturnsTrue()
    {
        FeishuMessageProcessor.ContainsMarkdown("功能：\n* 登录\n* 注册")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarkdown_OrderedList_ReturnsTrue()
    {
        FeishuMessageProcessor.ContainsMarkdown("步骤：\n1. 安装依赖\n2. 启动服务")
            .Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════
    // BuildCardJson — 输出结构验证
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildCardJson_Schema_Is_2_0()
    {
        string json = FeishuMessageProcessor.BuildCardJson("任意文本");
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("schema").GetString()
            .Should().Be("2.0");
    }

    [Fact]
    public void BuildCardJson_Tag_Is_Markdown()
    {
        string json = FeishuMessageProcessor.BuildCardJson("任意文本");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement elements = doc.RootElement
            .GetProperty("body")
            .GetProperty("elements");
        elements.GetArrayLength().Should().Be(1);
        elements[0].GetProperty("tag").GetString()
            .Should().Be("markdown");
    }

    [Fact]
    public void BuildCardJson_Content_Preserved()
    {
        const string original = "# 标题\n\n- 条目1\n- 条目2";
        string json = FeishuMessageProcessor.BuildCardJson(original);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement
            .GetProperty("body")
            .GetProperty("elements")[0]
            .GetProperty("content")
            .GetString()
            .Should().Be(original);
    }

    [Fact]
    public void BuildCardJson_SpecialChars_ProperlyEscaped()
    {
        // 包含双引号、反斜杠等特殊字符时，输出仍为合法 JSON
        const string text = "说明：\"引号\" 和 \\反斜杠\\ 都需要转义";
        string json = FeishuMessageProcessor.BuildCardJson(text);
        Action parse = () => JsonDocument.Parse(json).Dispose();
        parse.Should().NotThrow();

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement
            .GetProperty("body")
            .GetProperty("elements")[0]
            .GetProperty("content")
            .GetString()
            .Should().Be(text);
    }

    [Fact]
    public void BuildCardJson_Multiline_ContentPreserved()
    {
        const string text = "第一行\n第二行\n第三行";
        string json = FeishuMessageProcessor.BuildCardJson(text);
        using JsonDocument doc = JsonDocument.Parse(json);
        string? content = doc.RootElement
            .GetProperty("body")
            .GetProperty("elements")[0]
            .GetProperty("content")
            .GetString();
        content.Should().Contain("\n");
        content.Should().Be(text);
    }
}
