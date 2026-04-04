using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetDecisionEngine 单元测试：
/// - ParseDispatchResult：各种 JSON 输入的容错解析
/// - ExtractJson：Markdown 代码块提取
/// - DecideAsync：速率超限 / 无 Provider / 正常 LLM 调用
/// - PetDecisionEnginePrompt：System/User Prompt 构建
/// </summary>
[Collection("Config")]
public sealed class PetDecisionEngineTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;

    private const string SessionId = "decision-test-session";

    public PetDecisionEngineTests()
    {
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    #region ParseDispatchResult Tests

    [Fact]
    public void ParseDispatchResult_ValidJson_DelegateToAgent()
    {
        const string json = """
            {
              "agentId": "agent-code",
              "providerId": "gpt4",
              "toolOverrides": [],
              "petKnowledge": null,
              "shouldPetRespond": false,
              "petResponse": null,
              "reason": "用户需要代码帮助"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.AgentId.Should().Be("agent-code");
        result.ProviderId.Should().Be("gpt4");
        result.ToolOverrides.Should().BeEmpty();
        result.PetKnowledge.Should().BeNull();
        result.ShouldPetRespond.Should().BeFalse();
        result.PetResponse.Should().BeNull();
        result.Reason.Should().Be("用户需要代码帮助");
    }

    [Fact]
    public void ParseDispatchResult_PetSelfResponse()
    {
        const string json = """
            {
              "agentId": null,
              "providerId": null,
              "toolOverrides": [],
              "petKnowledge": null,
              "shouldPetRespond": true,
              "petResponse": "我现在心情不错！警觉度75，好奇心80。",
              "reason": "用户询问 Pet 状态"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.ShouldPetRespond.Should().BeTrue();
        result.PetResponse.Should().Be("我现在心情不错！警觉度75，好奇心80。");
        result.AgentId.Should().BeNull();
    }

    [Fact]
    public void ParseDispatchResult_WithToolOverrides()
    {
        const string json = """
            {
              "agentId": null,
              "providerId": null,
              "toolOverrides": [
                { "groupId": "cron", "isEnabled": false, "disabledToolNames": [] },
                { "groupId": "web-search", "isEnabled": true, "disabledToolNames": ["unsafe_tool"] }
              ],
              "petKnowledge": "用户之前提到喜欢Python",
              "shouldPetRespond": false,
              "petResponse": null,
              "reason": "禁用定时任务工具，启用网络搜索"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.ToolOverrides.Should().HaveCount(2);
        result.ToolOverrides[0].GroupId.Should().Be("cron");
        result.ToolOverrides[0].IsEnabled.Should().BeFalse();
        result.ToolOverrides[1].GroupId.Should().Be("web-search");
        result.ToolOverrides[1].IsEnabled.Should().BeTrue();
        result.ToolOverrides[1].DisabledToolNames.Should().Contain("unsafe_tool");
        result.PetKnowledge.Should().Be("用户之前提到喜欢Python");
    }

    [Fact]
    public void ParseDispatchResult_WithKnowledgeInjection()
    {
        const string json = """
            {
              "agentId": "agent-main",
              "providerId": null,
              "toolOverrides": [],
              "petKnowledge": "用户在之前的对话中提到他正在开发一个 C# 项目，使用 .NET 10。",
              "shouldPetRespond": false,
              "petResponse": null,
              "reason": "注入相关背景知识"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.AgentId.Should().Be("agent-main");
        result.PetKnowledge.Should().Contain("C# 项目");
    }

    [Fact]
    public void ParseDispatchResult_WrappedInMarkdownCodeBlock()
    {
        const string text = """
            ```json
            {
              "agentId": null,
              "providerId": null,
              "toolOverrides": [],
              "petKnowledge": null,
              "shouldPetRespond": false,
              "petResponse": null,
              "reason": "默认委派"
            }
            ```
            """;

        var result = PetDecisionEngine.ParseDispatchResult(text);

        result.ShouldPetRespond.Should().BeFalse();
        result.Reason.Should().Be("默认委派");
    }

    [Fact]
    public void ParseDispatchResult_EmptyResponse_ReturnsFallback()
    {
        var result = PetDecisionEngine.ParseDispatchResult("");

        result.ShouldPetRespond.Should().BeFalse();
        result.AgentId.Should().BeNull();
        result.Reason.Should().Contain("回退");
    }

    [Fact]
    public void ParseDispatchResult_InvalidJson_ReturnsFallback()
    {
        var result = PetDecisionEngine.ParseDispatchResult("not json at all");

        result.ShouldPetRespond.Should().BeFalse();
        result.AgentId.Should().BeNull();
        result.Reason.Should().Contain("回退");
    }

    [Fact]
    public void ParseDispatchResult_NullStringValues_TreatedAsNull()
    {
        const string json = """
            {
              "agentId": "null",
              "providerId": "",
              "toolOverrides": [],
              "petKnowledge": "null",
              "shouldPetRespond": false,
              "petResponse": "",
              "reason": "测试空值处理"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.AgentId.Should().BeNull();
        result.ProviderId.Should().BeNull();
        result.PetKnowledge.Should().BeNull();
        result.PetResponse.Should().BeNull();
    }

    [Fact]
    public void ParseDispatchResult_MissingFields_UsesDefaults()
    {
        const string json = """
            {
              "reason": "最简 JSON"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.AgentId.Should().BeNull();
        result.ProviderId.Should().BeNull();
        result.ToolOverrides.Should().BeEmpty();
        result.ShouldPetRespond.Should().BeFalse();
        result.Reason.Should().Be("最简 JSON");
    }

    [Fact]
    public void ParseDispatchResult_InvalidToolOverrideGroupId_Skipped()
    {
        const string json = """
            {
              "agentId": null,
              "toolOverrides": [
                { "groupId": "", "isEnabled": true },
                { "groupId": "valid-group", "isEnabled": true }
              ],
              "shouldPetRespond": false,
              "reason": "跳过空 groupId"
            }
            """;

        var result = PetDecisionEngine.ParseDispatchResult(json);

        result.ToolOverrides.Should().HaveCount(1);
        result.ToolOverrides[0].GroupId.Should().Be("valid-group");
    }

    #endregion

    #region ExtractJson Tests

    [Fact]
    public void ExtractJson_PlainJson_ReturnsAsIs()
    {
        const string json = """{"key": "value"}""";
        PetDecisionEngine.ExtractJson(json).Should().Be(json);
    }

    [Fact]
    public void ExtractJson_MarkdownWrapped_ExtractsJson()
    {
        const string text = """
            ```json
            {"key": "value"}
            ```
            """;

        var extracted = PetDecisionEngine.ExtractJson(text);
        extracted.Should().Contain("\"key\"");
        extracted.Should().StartWith("{");
        extracted.Should().EndWith("}");
    }

    #endregion

    #region PetDecisionEnginePrompt Tests

    [Fact]
    public void BuildSystemPrompt_ContainsKeyElements()
    {
        string prompt = PetDecisionEnginePrompt.BuildSystemPrompt();

        prompt.Should().Contain("消息调度决策引擎");
        prompt.Should().Contain("agentId");
        prompt.Should().Contain("providerId");
        prompt.Should().Contain("toolOverrides");
        prompt.Should().Contain("shouldPetRespond");
        prompt.Should().Contain("petResponse");
        prompt.Should().Contain("petKnowledge");
        prompt.Should().Contain("JSON");
    }

    [Fact]
    public void BuildUserPrompt_ContainsUserMessage()
    {
        var context = CreateTestContext("你好，请帮我写一个排序算法");

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("你好，请帮我写一个排序算法");
        prompt.Should().Contain("用户消息");
    }

    [Fact]
    public void BuildUserPrompt_ContainsAvailableAgents()
    {
        var context = CreateTestContext("hello") with
        {
            AvailableAgents =
            [
                new AgentSummary("agent-1", "代码助手", "帮助写代码", IsDefault: true),
                new AgentSummary("agent-2", "翻译助手", "翻译文本", IsDefault: false),
            ],
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("agent-1");
        prompt.Should().Contain("代码助手");
        prompt.Should().Contain("[默认]");
        prompt.Should().Contain("agent-2");
        prompt.Should().Contain("翻译助手");
    }

    [Fact]
    public void BuildUserPrompt_ContainsAvailableProviders()
    {
        var context = CreateTestContext("hello") with
        {
            AvailableProviders =
            [
                new ProviderSummary("gpt4", "GPT-4o", "gpt-4o", 90, "Low", 2.5m, 10m, IsDefault: true),
            ],
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("gpt4");
        prompt.Should().Contain("GPT-4o");
        prompt.Should().Contain("质量=90");
    }

    [Fact]
    public void BuildUserPrompt_ContainsToolGroups()
    {
        var context = CreateTestContext("hello") with
        {
            AvailableToolGroups = ["cron - 定时任务", "web-search - 网络搜索"],
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("cron - 定时任务");
        prompt.Should().Contain("web-search - 网络搜索");
    }

    [Fact]
    public void BuildUserPrompt_ContainsPetState()
    {
        var context = CreateTestContext("hello") with
        {
            BehaviorState = PetBehaviorState.Learning,
            EmotionState = new EmotionState(80, 70, 90, 60),
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("Learning");
        prompt.Should().Contain("警觉度=80");
        prompt.Should().Contain("好奇心=90");
    }

    [Fact]
    public void BuildUserPrompt_ContainsRateLimitWarning_WhenExhausted()
    {
        var context = CreateTestContext("hello") with
        {
            RateLimitStatus = new RateLimitStatus(100, 100, 0,
                DateTimeOffset.UtcNow.AddHours(-4),
                DateTimeOffset.UtcNow.AddHours(1),
                IsExhausted: true),
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("配额已耗尽");
    }

    [Fact]
    public void BuildUserPrompt_ContainsPetRagKnowledge()
    {
        var context = CreateTestContext("hello") with
        {
            PetRagKnowledge = "用户偏好 Python 语言，习惯使用 pytest",
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("Pet 私有知识");
        prompt.Should().Contain("Python 语言");
    }

    [Fact]
    public void BuildUserPrompt_ContainsRecentMessages()
    {
        var context = CreateTestContext("hello") with
        {
            RecentMessageSummaries = ["[user] 你好", "[assistant] 你好！有什么可以帮助你的？"],
        };

        string prompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        prompt.Should().Contain("最近会话上下文");
        prompt.Should().Contain("[user] 你好");
    }

    [Fact]
    public void BuildUserPrompt_NullContext_Throws()
    {
        var act = () => PetDecisionEnginePrompt.BuildUserPrompt(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region DecideAsync Tests

    [Fact]
    public async Task DecideAsync_RateLimitExhausted_ReturnsDefaultDispatch()
    {
        // 设置 Pet 状态并耗尽配额
        await SetupPetAsync(maxCalls: 1);

        // 先消耗唯一配额
        var rateLimiter = new PetRateLimiter(_stateStore);
        bool first = await rateLimiter.TryAcquireAsync(SessionId);
        first.Should().BeTrue();

        // 创建 engine
        TestConfigFixture.EnsureInitialized();
        var providerStore = new ProviderConfigStore();
        var router = new ProviderRouter();
        var modelSelector = new PetModelSelector(providerStore, router);
        var clientFactory = new ProviderClientFactory([]);
        var engine = new PetDecisionEngine(
            rateLimiter, modelSelector, _stateStore, _emotionStore,
            clientFactory, NullLogger<PetDecisionEngine>.Instance);

        var context = CreateTestContext("test message");
        var result = await engine.DecideAsync(context, null, SessionId);

        result.ShouldPetRespond.Should().BeFalse();
        result.AgentId.Should().BeNull();
        result.Reason.Should().Contain("速率");
    }

    #endregion

    #region Helpers

    private async Task SetupPetAsync(int maxCalls = 100)
    {
        var state = new PetState
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Idle,
        };
        await _stateStore.SaveAsync(state);
        await _stateStore.SaveConfigAsync(SessionId, new PetConfig
        {
            Enabled = true,
            MaxLlmCallsPerWindow = maxCalls,
        });
        await _emotionStore.SaveAsync(SessionId, EmotionState.Default);
    }

    private static PetDecisionContext CreateTestContext(string userMessage) => new()
    {
        UserMessage = userMessage,
        BehaviorState = PetBehaviorState.Idle,
        EmotionState = EmotionState.Default,
        AvailableAgents =
        [
            new AgentSummary("agent-default", "默认助手", "通用对话", IsDefault: true),
        ],
        AvailableProviders =
        [
            new ProviderSummary("gpt4", "GPT-4o", "gpt-4o", 90, "Low", 2.5m, 10m, IsDefault: true),
        ],
    };

    #endregion
}
