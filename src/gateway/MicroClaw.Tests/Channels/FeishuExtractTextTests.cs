using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels.Feishu;

namespace MicroClaw.Tests.Channels;

/// <summary>
/// F-H-1: 单元测试 — FeishuMessageProcessor.ExtractText 消息类型过滤与文本提取。
/// 覆盖：text / image / file / post / 未知类型 / 空入参 / 畸形 JSON。
/// </summary>
public sealed class FeishuExtractTextTests
{
    // ─── 辅助构造 ────────────────────────────────────────────────────────────

    private static FeishuMessageEvent MakeEvent(string messageType, string contentJson) =>
        new()
        {
            Message = new FeishuMessageBody
            {
                MessageType = messageType,
                Content = contentJson
            }
        };

    // ─── 边界 / null 保护 ─────────────────────────────────────────────────

    [Fact]
    public void ExtractText_NullEvent_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText((FeishuMessageEvent?)null)
            .Should().BeNull();
    }

    [Fact]
    public void ExtractText_NullMessage_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText(new FeishuMessageEvent { Message = null })
            .Should().BeNull();
    }

    [Fact]
    public void ExtractText_EmptyContent_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText(MakeEvent("text", ""))
            .Should().BeNull();
    }

    [Fact]
    public void ExtractText_WhitespaceContent_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText(MakeEvent("text", "   "))
            .Should().BeNull();
    }

    // ─── text 类型 ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractText_PlainText_ReturnsText()
    {
        string json = JsonSerializer.Serialize(new { text = "Hello World" });
        FeishuMessageProcessor.ExtractText(MakeEvent("text", json))
            .Should().Be("Hello World");
    }

    [Fact]
    public void ExtractText_TextWithAtMention_StripsAtMark()
    {
        // 飞书文本消息中 @_user_1 是标准 mention 占位符
        string json = JsonSerializer.Serialize(new { text = "@_user_1 帮我写份报告" });
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("text", json));
        result.Should().NotContain("@_user_1");
        result.Should().Contain("帮我写份报告");
    }

    [Fact]
    public void ExtractText_TextOnlyWhitespaceAfterStripMentions_ReturnsNull()
    {
        // 整条消息只有 mention，去除后为空
        string json = JsonSerializer.Serialize(new { text = "@_user_1" });
        FeishuMessageProcessor.ExtractText(MakeEvent("text", json))
            .Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void ExtractText_MalformedTextJson_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText(MakeEvent("text", "not-valid-json{{{"))
            .Should().BeNull();
    }

    // ─── image 类型 ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractText_ImageWithKey_ReturnsImageKeyDescription()
    {
        string json = JsonSerializer.Serialize(new { image_key = "img_abc123" });
        FeishuMessageProcessor.ExtractText(MakeEvent("image", json))
            .Should().Be("[图片: img_abc123]");
    }

    [Fact]
    public void ExtractText_ImageWithoutKey_ReturnsFallbackDescription()
    {
        string json = JsonSerializer.Serialize(new { image_key = (string?)null });
        FeishuMessageProcessor.ExtractText(MakeEvent("image", json))
            .Should().Be("[图片]");
    }

    // ─── file 类型 ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractText_FileWithNameAndSize_ReturnsFormattedDescription()
    {
        // 2048 bytes → 2KB
        string json = JsonSerializer.Serialize(new { file_name = "report.pdf", file_size = 2048L });
        FeishuMessageProcessor.ExtractText(MakeEvent("file", json))
            .Should().Be("[文件: report.pdf, 2KB]");
    }

    [Fact]
    public void ExtractText_FileLargerThanOneMb_ShowsMbUnit()
    {
        // 2 * 1024 * 1024 = 2097152 bytes → 2.0MB
        string json = JsonSerializer.Serialize(new { file_name = "video.mp4", file_size = 2097152L });
        FeishuMessageProcessor.ExtractText(MakeEvent("file", json))
            .Should().Be("[文件: video.mp4, 2.0MB]");
    }

    [Fact]
    public void ExtractText_FileWithoutSize_ReturnsNameOnly()
    {
        string json = JsonSerializer.Serialize(new { file_name = "doc.docx", file_size = (long?)null });
        FeishuMessageProcessor.ExtractText(MakeEvent("file", json))
            .Should().Be("[文件: doc.docx]");
    }

    [Fact]
    public void ExtractText_FileWithoutName_UsesUnknownFallback()
    {
        string json = JsonSerializer.Serialize(new { file_name = (string?)null });
        FeishuMessageProcessor.ExtractText(MakeEvent("file", json))
            .Should().StartWith("[文件: 未知文件]");
    }

    // ─── post 富文本类型 ──────────────────────────────────────────────────

    [Fact]
    public void ExtractText_PostZhCn_ExtractsTitleAndParagraphs()
    {
        var content = new
        {
            zh_cn = new
            {
                title = "周报",
                content = new[]
                {
                    new[] { new { tag = "text", text = "本周完成了功能开发" } },
                    new[] { new { tag = "text", text = "下周继续测试" } }
                }
            }
        };
        string json = JsonSerializer.Serialize(content);
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("post", json));
        result.Should().Contain("周报");
        result.Should().Contain("本周完成了功能开发");
        result.Should().Contain("下周继续测试");
    }

    [Fact]
    public void ExtractText_PostEnUsWhenNoZhCn_FallsBackToEnglishContent()
    {
        var content = new
        {
            en_us = new
            {
                title = "Weekly Report",
                content = new[]
                {
                    new[] { new { tag = "text", text = "Feature done" } }
                }
            }
        };
        string json = JsonSerializer.Serialize(content);
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("post", json));
        result.Should().Contain("Weekly Report");
        result.Should().Contain("Feature done");
    }

    [Fact]
    public void ExtractText_PostWithLinkElement_ExtractsLinkText()
    {
        var content = new
        {
            zh_cn = new
            {
                title = "",
                content = new[]
                {
                    new[] { new { tag = "a", text = "飞书官网", href = "https://feishu.cn" } }
                }
            }
        };
        string json = JsonSerializer.Serialize(content);
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("post", json));
        result.Should().Contain("飞书官网");
    }

    [Fact]
    public void ExtractText_PostWithAtElement_ExtractsUserName()
    {
        var content = new
        {
            zh_cn = new
            {
                title = "",
                content = new[]
                {
                    new[]
                    {
                        new { tag = "at", user_id = "ou_abc", user_name = "张三", text = (string?)null, href = (string?)null }
                    }
                }
            }
        };
        string json = JsonSerializer.Serialize(content);
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("post", json));
        result.Should().Contain("@张三");
    }

    [Fact]
    public void ExtractText_PostWithEmptyBody_ReturnsNull()
    {
        string json = JsonSerializer.Serialize(new { zh_cn = (object?)null, en_us = (object?)null });
        FeishuMessageProcessor.ExtractText(MakeEvent("post", json))
            .Should().BeNull();
    }

    // ─── 未知类型 ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractText_UnknownMessageType_ReturnsNull()
    {
        string json = JsonSerializer.Serialize(new { some_field = "data" });
        FeishuMessageProcessor.ExtractText(MakeEvent("audio", json))
            .Should().BeNull();
    }

    [Fact]
    public void ExtractText_NullMessageType_ReturnsNull()
    {
        FeishuMessageProcessor.ExtractText(
            new FeishuMessageEvent { Message = new FeishuMessageBody { MessageType = null, Content = "{}" } })
            .Should().BeNull();
    }

    // ─── F-A-7: ResolveMentions ──────────────────────────────────────────

    [Fact]
    public void ResolveMentions_NullMap_StripsPlaceholder()
    {
        // 无映射时退回旧行为（去除 @_user_N 占位符）
        FeishuMessageProcessor.ResolveMentions("@_user_1 帮我查询", null)
            .Should().Be("帮我查询");
    }

    [Fact]
    public void ResolveMentions_WithMap_ReplacesPlaceholderWithName()
    {
        var map = new Dictionary<string, string> { ["@_user_1"] = "机器人小助手" };
        FeishuMessageProcessor.ResolveMentions("@_user_1 请帮我总结", map)
            .Should().Be("@机器人小助手 请帮我总结");
    }

    [Fact]
    public void ResolveMentions_MultipleWithMap_ReplacesAll()
    {
        var map = new Dictionary<string, string>
        {
            ["@_user_1"] = "Bot",
            ["@_user_2"] = "张三"
        };
        string result = FeishuMessageProcessor.ResolveMentions("@_user_1 请帮 @_user_2 写周报", map);
        result.Should().Be("@Bot 请帮 @张三 写周报");
    }

    [Fact]
    public void ResolveMentions_KeyNotInMap_StripsUnmatchedPlaceholder()
    {
        // map 中没有 @_user_2 的映射，应直接去除
        var map = new Dictionary<string, string> { ["@_user_1"] = "Bot" };
        string result = FeishuMessageProcessor.ResolveMentions("@_user_1 帮 @_user_2 写", map);
        result.Should().Contain("@Bot");
        result.Should().NotContain("@_user_2");
    }

    [Fact]
    public void ResolveMentions_EmptyMap_StripsAllPlaceholders()
    {
        var map = new Dictionary<string, string>();
        FeishuMessageProcessor.ResolveMentions("@_user_1 你好", map)
            .Should().Be("你好");
    }

    // ─── F-A-7: ExtractText with Mentions (端到端) ───────────────────────

    [Fact]
    public void ExtractText_TextWithMentionsArray_ReplacesAtUserWithName()
    {
        // 群聊中消息: "@_user_1 请帮我查一下" + Mentions[0] = {Key: "@_user_1", Name: "小助手"}
        string contentJson = JsonSerializer.Serialize(new { text = "@_user_1 请帮我查一下" });
        var evt = new FeishuMessageEvent
        {
            Message = new FeishuMessageBody
            {
                MessageType = "text",
                Content = contentJson,
                Mentions =
                [
                    new FeishuMention
                    {
                        Key = "@_user_1",
                        Name = "小助手",
                        Id = new FeishuMentionId { OpenId = "ou_xxx" }
                    }
                ]
            }
        };

        string? result = FeishuMessageProcessor.ExtractText(evt);
        result.Should().Be("@小助手 请帮我查一下");
    }

    [Fact]
    public void ExtractText_TextNoMentionsArray_StillStripsPlaceholder()
    {
        // 无 Mentions 数组时退回旧行为
        string contentJson = JsonSerializer.Serialize(new { text = "@_user_1 hello" });
        string? result = FeishuMessageProcessor.ExtractText(MakeEvent("text", contentJson));
        result.Should().Be("hello");
    }

    [Fact]
    public void ExtractText_TextMentionFallbackToOpenId_WhenNameEmpty()
    {
        // Name 为空时，使用 openId 作为显示名
        string contentJson = JsonSerializer.Serialize(new { text = "@_user_1 请回答" });
        var evt = new FeishuMessageEvent
        {
            Message = new FeishuMessageBody
            {
                MessageType = "text",
                Content = contentJson,
                Mentions =
                [
                    new FeishuMention
                    {
                        Key = "@_user_1",
                        Name = null,
                        Id = new FeishuMentionId { OpenId = "ou_abc123" }
                    }
                ]
            }
        };

        string? result = FeishuMessageProcessor.ExtractText(evt);
        result.Should().Be("@ou_abc123 请回答");
    }
}
