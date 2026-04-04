using FluentAssertions;
using MicroClaw.Pet.Prompt;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetPromptEvolver 单元测试：
/// - ParseEvolution 解析有效 JSON
/// - ParseEvolution 解析 Markdown 代码块包裹
/// - ParseEvolution 空/无效响应返回 null
/// - BuildSystemPrompt / BuildUserPrompt 格式验证
/// </summary>
public sealed class PetPromptEvolverTests
{
    // ── ParseEvolution ───────────────────────────────────────────────────

    [Fact]
    public void ParseEvolution_ValidJson_ReturnsCorrectResult()
    {
        var json = """
        {
          "summary": "调整语气为友好模式",
          "personality": {
            "persona": null,
            "tone": "friendly",
            "language": null
          },
          "dispatch_rules": null,
          "knowledge_interests": null
        }
        """;

        var result = PetPromptEvolver.ParseEvolution(json);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("调整语气为友好模式");
        result.Personality.Should().NotBeNull();
        result.Personality!.Tone.Should().Be("friendly");
        result.Personality.Persona.Should().BeNull();
        result.DispatchRules.Should().BeNull();
        result.KnowledgeInterests.Should().BeNull();
    }

    [Fact]
    public void ParseEvolution_MarkdownCodeBlock_ExtractsCorrectly()
    {
        var response = """
        根据分析，建议以下调整：

        ```json
        {
          "summary": "增加代码审查规则",
          "personality": null,
          "dispatch_rules": {
            "default_strategy": "quality",
            "rules": [
              {"pattern": ".*review.*", "preferred_model_type": "quality", "notes": "代码审查"}
            ]
          },
          "knowledge_interests": null
        }
        ```

        以上调整基于最近日志中频繁出现代码审查相关请求。
        """;

        var result = PetPromptEvolver.ParseEvolution(response);

        result.Should().NotBeNull();
        result!.Summary.Should().Contain("代码审查");
        result.DispatchRules.Should().NotBeNull();
        result.DispatchRules!.DefaultStrategy.Should().Be("quality");
        result.DispatchRules.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void ParseEvolution_PlainJsonInText_ExtractsBraces()
    {
        var response = """
        建议如下修改：
        {"summary": "测试", "personality": null, "dispatch_rules": null, "knowledge_interests": null}
        结束。
        """;

        var result = PetPromptEvolver.ParseEvolution(response);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("测试");
    }

    [Fact]
    public void ParseEvolution_EmptyResponse_ReturnsNull()
    {
        PetPromptEvolver.ParseEvolution("").Should().BeNull();
        PetPromptEvolver.ParseEvolution("   ").Should().BeNull();
    }

    [Fact]
    public void ParseEvolution_InvalidJson_ReturnsNull()
    {
        PetPromptEvolver.ParseEvolution("这不是 JSON").Should().BeNull();
        PetPromptEvolver.ParseEvolution("{ invalid json }").Should().BeNull();
    }

    [Fact]
    public void ParseEvolution_AllFieldsPresent_ParsesCompletely()
    {
        var json = """
        {
          "summary": "全面更新",
          "personality": {
            "persona": "新人格",
            "tone": "casual",
            "language": "en"
          },
          "dispatch_rules": {
            "default_strategy": "cost",
            "rules": [
              {"pattern": ".*", "preferred_model_type": "cost", "notes": "全部低成本"}
            ]
          },
          "knowledge_interests": {
            "topics": [
              {"name": "new_topic", "description": "新主题", "priority": "high"}
            ]
          }
        }
        """;

        var result = PetPromptEvolver.ParseEvolution(json);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("全面更新");
        result.Personality!.Persona.Should().Be("新人格");
        result.DispatchRules!.Rules.Should().HaveCount(1);
        result.KnowledgeInterests!.Topics.Should().HaveCount(1);
        result.KnowledgeInterests.Topics![0].Name.Should().Be("new_topic");
    }

    // ── BuildSystemPrompt ────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_ContainsKeyElements()
    {
        var prompt = PetPromptEvolver.BuildSystemPrompt();

        prompt.Should().Contain("进化原则");
        prompt.Should().Contain("JSON");
        prompt.Should().Contain("personality");
        prompt.Should().Contain("dispatch_rules");
        prompt.Should().Contain("knowledge_interests");
    }

    // ── BuildUserPrompt ──────────────────────────────────────────────────

    [Fact]
    public void BuildUserPrompt_ContainsAllSections()
    {
        var prompt = PetPromptEvolver.BuildUserPrompt(
            currentPrompts: "当前提示词内容",
            recentJournal: "journal line 1\njournal line 2",
            recentHabits: "habit data",
            reason: "测试原因");

        prompt.Should().Contain("当前提示词");
        prompt.Should().Contain("当前提示词内容");
        prompt.Should().Contain("行为日志");
        prompt.Should().Contain("journal line 1");
        prompt.Should().Contain("会话习惯");
        prompt.Should().Contain("habit data");
        prompt.Should().Contain("触发原因");
        prompt.Should().Contain("测试原因");
    }

    [Fact]
    public void BuildUserPrompt_OmitsReason_WhenNull()
    {
        var prompt = PetPromptEvolver.BuildUserPrompt(
            currentPrompts: "提示词",
            recentJournal: "日志",
            recentHabits: "习惯",
            reason: null);

        prompt.Should().NotContain("触发原因");
    }
}
