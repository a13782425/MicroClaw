using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// P-I-6 集成测试：Session 审批 → PetFactory 自动创建 Pet → 文件系统初始化正确。
/// </summary>
[Collection("Config")]
public sealed class PetFactoryIntegrationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly string _sessionsDir;

    public PetFactoryIntegrationTests()
    {
        // 设置 MICROCLAW_HOME，使得 SessionsDir = _tempDir/workspace/sessions
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempDir.Path);
        TestConfigFixture.EnsureInitialized();
        _sessionsDir = MicroClawConfig.Env.SessionsDir;
        // PetStateStore 使用与 PetFactory 相同的 sessionsDir
        _stateStore = new PetStateStore(_sessionsDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        _tempDir.Dispose();
    }

    [Fact]
    public async Task CreateAsync_InitializesFullDirectoryStructure()
    {
        // Arrange
        string sessionId = "factory-test-1";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert: 目录存在
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
        Directory.Exists(petDir).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WritesStateJson()
    {
        // Arrange
        string sessionId = "factory-test-state";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert: state.json 存在且可加载
        var state = await _stateStore.LoadAsync(sessionId);
        state.Should().NotBeNull();
        state!.SessionId.Should().Be(sessionId);
        state.BehaviorState.Should().Be(PetBehaviorState.Idle);
        state.EmotionState.Should().Be(EmotionState.Default);
        state.LlmCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WritesConfigJson()
    {
        // Arrange
        string sessionId = "factory-test-config";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert: config.json 存在且可加载
        var config = await _stateStore.LoadConfigAsync(sessionId);
        config.Should().NotBeNull();
        config!.Enabled.Should().BeFalse("默认配置 Enabled = false");
        config.MaxLlmCallsPerWindow.Should().Be(100);
        config.WindowHours.Should().Be(5.0);
    }

    [Fact]
    public async Task CreateAsync_WritesYamlTemplates()
    {
        // Arrange
        string sessionId = "factory-test-yaml";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert: 三个 YAML 文件存在
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
        File.Exists(Path.Combine(petDir, "personality.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(petDir, "dispatch-rules.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(petDir, "knowledge-interests.yaml")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_PersonalityYamlContainsDefaultContent()
    {
        // Arrange
        string sessionId = "factory-test-yaml-content";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert: personality.yaml 内容合法
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
        string content = await File.ReadAllTextAsync(Path.Combine(petDir, "personality.yaml"));
        content.Should().Contain("persona");
        content.Should().Contain("tone");
        content.Should().Contain("language");
    }

    [Fact]
    public async Task CreateAsync_DispatchRulesYamlContainsDefaultContent()
    {
        // Arrange
        string sessionId = "factory-test-dispatch";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
        string content = await File.ReadAllTextAsync(Path.Combine(petDir, "dispatch-rules.yaml"));
        content.Should().Contain("default_strategy");
        content.Should().Contain("rules");
    }

    [Fact]
    public async Task CreateAsync_KnowledgeInterestsYamlContainsDefaultContent()
    {
        // Arrange
        string sessionId = "factory-test-interests";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");
        string content = await File.ReadAllTextAsync(Path.Combine(petDir, "knowledge-interests.yaml"));
        content.Should().Contain("topics");
    }

    [Fact]
    public async Task CreateAsync_IdempotentDoesNotOverwrite()
    {
        // Arrange: 先创建
        string sessionId = "factory-test-idempotent";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);
        await factory.CreateOrLoadAsync(microSession);

        // 修改 state
        var originalState = await _stateStore.LoadAsync(sessionId);
        var modifiedState = originalState! with
        {
            BehaviorState = PetBehaviorState.Learning,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(modifiedState);

        // Act: 再次调用，应跳过
        await factory.CreateOrLoadAsync(microSession);

        // Assert: state 未被重置
        var reloaded = await _stateStore.LoadAsync(sessionId);
        reloaded!.BehaviorState.Should().Be(PetBehaviorState.Learning);
    }

    [Fact]
    public async Task CreateAsync_WithCustomConfig()
    {
        // Arrange
        string sessionId = "factory-test-custom-config";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);
        var customConfig = new PetConfig
        {
            Enabled = true,
            MaxLlmCallsPerWindow = 50,
            WindowHours = 2.0,
            SocialMode = true,
        };

        // Act
        await factory.CreateOrLoadAsync(microSession, customConfig);

        // Assert: config 应包含自定义值
        var config = await _stateStore.LoadConfigAsync(sessionId);
        config.Should().NotBeNull();
        config!.Enabled.Should().BeTrue();
        config.MaxLlmCallsPerWindow.Should().Be(50);
        config.WindowHours.Should().Be(2.0);
        config.SocialMode.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_EmptySessionId_ThrowsArgument()
    {
        var factory = CreateFactory();
        MicroSession microSession = CreateSession("unused");

        var act = () => factory.CreateOrLoadAsync(CreateSession(""), ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_StateTimestampsAreReasonable()
    {
        // Arrange
        string sessionId = "factory-test-timestamps";
        var factory = CreateFactory();
        MicroSession microSession = CreateSession(sessionId);
        var before = DateTimeOffset.UtcNow;

        // Act
        await factory.CreateOrLoadAsync(microSession);

        // Assert
        var state = await _stateStore.LoadAsync(sessionId);
        state.Should().NotBeNull();
        state!.CreatedAt.Should().BeOnOrAfter(before);
        state.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        state.WindowStart.Should().BeOnOrAfter(before);
    }

    // ── 辅助方法 ────────────────────────────────────────────────────────

    private PetFactory CreateFactory()
    {
        EmotionStore emotionStore = new(_sessionsDir);
        PetContextFactory contextFactory = new(_stateStore, emotionStore);
        return new PetFactory(_stateStore, contextFactory, _sessionsDir, NullLogger<PetFactory>.Instance);
    }

    private static MicroSession CreateSession(string sessionId)
        => MicroSession.Reconstitute(
            id: sessionId,
            title: sessionId,
            providerId: "provider-1",
            isApproved: false,
            channelType: Configuration.Options.ChannelType.Web,
            channelId: "web",
            createdAt: DateTimeOffset.UtcNow);
}
