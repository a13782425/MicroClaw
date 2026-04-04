using FluentAssertions;
using MicroClaw.Pet.Prompt;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetPromptStore 单元测试：
/// - YAML 读写正确
/// - 默认值返回
/// - .bak 备份生成
/// - LoadAllAsText 聚合输出
/// </summary>
public sealed class PetPromptStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetPromptStore _store;

    private const string SessionId = "prompt-store-test-session";

    public PetPromptStoreTests()
    {
        _store = new PetPromptStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── Personality ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadPersonality_ReturnsDefault_WhenFileNotExists()
    {
        var result = await _store.LoadPersonalityAsync(SessionId);

        result.Should().NotBeNull();
        result.Tone.Should().Be("professional");
        result.Language.Should().Be("zh-cn");
        result.Persona.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveAndLoadPersonality_RoundTrips()
    {
        var prompt = new PersonalityPrompt
        {
            Persona = "你是一个测试助手。",
            Tone = "casual",
            Language = "en",
        };

        await _store.SavePersonalityAsync(SessionId, prompt);
        var loaded = await _store.LoadPersonalityAsync(SessionId);

        loaded.Persona.Should().Contain("测试助手");
        loaded.Tone.Should().Be("casual");
        loaded.Language.Should().Be("en");
    }

    [Fact]
    public async Task SavePersonality_CreatesBakFile()
    {
        var original = new PersonalityPrompt { Persona = "原始", Tone = "formal", Language = "zh-cn" };
        await _store.SavePersonalityAsync(SessionId, original);

        var updated = new PersonalityPrompt { Persona = "更新后", Tone = "casual", Language = "zh-cn" };
        await _store.SavePersonalityAsync(SessionId, updated);

        var bakPath = Path.Combine(_tempDir.Path, SessionId, "pet", "personality.yaml.bak");
        File.Exists(bakPath).Should().BeTrue();
    }

    // ── Dispatch Rules ───────────────────────────────────────────────────

    [Fact]
    public async Task LoadDispatchRules_ReturnsDefault_WhenFileNotExists()
    {
        var result = await _store.LoadDispatchRulesAsync(SessionId);

        result.Should().NotBeNull();
        result.DefaultStrategy.Should().Be("default");
        result.Rules.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SaveAndLoadDispatchRules_RoundTrips()
    {
        var rules = new DispatchRules
        {
            DefaultStrategy = "quality",
            Rules =
            [
                new DispatchRule { Pattern = ".*test.*", PreferredModelType = "cost", Notes = "test rule" },
            ],
        };

        await _store.SaveDispatchRulesAsync(SessionId, rules);
        var loaded = await _store.LoadDispatchRulesAsync(SessionId);

        loaded.DefaultStrategy.Should().Be("quality");
        loaded.Rules.Should().HaveCount(1);
        loaded.Rules[0].Pattern.Should().Be(".*test.*");
    }

    // ── Knowledge Interests ──────────────────────────────────────────────

    [Fact]
    public async Task LoadKnowledgeInterests_ReturnsDefault_WhenFileNotExists()
    {
        var result = await _store.LoadKnowledgeInterestsAsync(SessionId);

        result.Should().NotBeNull();
        result.Topics.Should().NotBeEmpty();
        result.Topics.Should().Contain(t => t.Name == "user_preferences");
    }

    [Fact]
    public async Task SaveAndLoadKnowledgeInterests_RoundTrips()
    {
        var interests = new KnowledgeInterests
        {
            Topics =
            [
                new KnowledgeTopic { Name = "testing", Description = "测试技术", Priority = "high" },
                new KnowledgeTopic { Name = "design", Description = "设计模式", Priority = "low" },
            ],
        };

        await _store.SaveKnowledgeInterestsAsync(SessionId, interests);
        var loaded = await _store.LoadKnowledgeInterestsAsync(SessionId);

        loaded.Topics.Should().HaveCount(2);
        loaded.Topics[0].Name.Should().Be("testing");
        loaded.Topics[1].Priority.Should().Be("low");
    }

    // ── LoadAllAsText ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAllAsText_ContainsAllSections()
    {
        var text = await _store.LoadAllAsTextAsync(SessionId);

        text.Should().Contain("人格设定");
        text.Should().Contain("调度规则");
        text.Should().Contain("学习方向");
    }

    [Fact]
    public async Task LoadAllAsText_ReflectsCustomPrompts()
    {
        await _store.SavePersonalityAsync(SessionId, new PersonalityPrompt
        {
            Persona = "自定义人格",
            Tone = "friendly",
            Language = "en",
        });

        var text = await _store.LoadAllAsTextAsync(SessionId);

        text.Should().Contain("自定义人格");
        text.Should().Contain("friendly");
    }

    // ── 边界情况 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SavePersonality_CreatesDirectoryIfNeeded()
    {
        const string newSession = "brand-new-session";
        var prompt = new PersonalityPrompt { Persona = "test", Tone = "test", Language = "test" };

        await _store.SavePersonalityAsync(newSession, prompt);

        var petDir = Path.Combine(_tempDir.Path, newSession, "pet");
        Directory.Exists(petDir).Should().BeTrue();
    }

    [Fact]
    public async Task OverwriteExistingFile_PreservesBackup()
    {
        var v1 = new DispatchRules { DefaultStrategy = "v1", Rules = [] };
        await _store.SaveDispatchRulesAsync(SessionId, v1);

        var v2 = new DispatchRules { DefaultStrategy = "v2", Rules = [] };
        await _store.SaveDispatchRulesAsync(SessionId, v2);

        var v3 = new DispatchRules { DefaultStrategy = "v3", Rules = [] };
        await _store.SaveDispatchRulesAsync(SessionId, v3);

        // .bak should contain v2 (the previous version before v3)
        var bakPath = Path.Combine(_tempDir.Path, SessionId, "pet", "dispatch-rules.yaml.bak");
        var bakContent = await File.ReadAllTextAsync(bakPath);
        bakContent.Should().Contain("v2");
    }
}
