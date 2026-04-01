using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Channels;

/// <summary>
/// F-H-2: 单元测试 — FeishuMessageProcessor 消息去重（F-A-2）。
/// 通过反射访问私有字典 _processedMessageIds，验证幂等性与惰性清理行为。
/// </summary>
public sealed class FeishuDeduplicationTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly FeishuMessageProcessor _processor;
    private readonly ChannelConfig _channel;
    private readonly FeishuChannelSettings _settings;

    // 通过反射获取私有 _processedMessageIds 字典，用于直接断言内部状态
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenIds;

    public FeishuDeduplicationTests()
    {
        // ProviderConfigStore 使用空内存 DB → providerConfig 查找失败 → provider 检查处提前返回
        ProviderConfigStore providerStore = new(_db.CreateFactory());
        ProviderClientFactory clientFactory = new([]);
        IChannelSessionService sessionService = Substitute.For<IChannelSessionService>();
        ILogger<FeishuMessageProcessor> logger = Substitute.For<ILogger<FeishuMessageProcessor>>();

        // FindOrCreateSession 返回一个无绑定模型的会话，使 provider 回退逻辑最终找不到已启用 Provider 而提前返回
        sessionService.FindOrCreateSession(
            Arg.Any<ChannelType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>())
            .Returns(new SessionInfo("sess-dedup", "Test", "", false,
                ChannelType.Feishu, "ch-dedup-test", DateTimeOffset.UtcNow));

        _processor = new FeishuMessageProcessor(providerStore, clientFactory, sessionService, logger);

        FieldInfo field = typeof(FeishuMessageProcessor)
            .GetField("_processedMessageIds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _seenIds = (ConcurrentDictionary<string, DateTimeOffset>)field.GetValue(_processor)!;

        _channel = new ChannelConfig
        {
            Id = "ch-dedup-test",
            DisplayName = "Dedup Test Channel",
            ChannelType = ChannelType.Feishu,
            IsEnabled = true,
            SettingsJson = JsonSerializer.Serialize(new FeishuChannelSettings
            {
                AppId = "cli_test",
                AppSecret = "secret",
                ConnectionMode = "websocket"
            })
        };
        _settings = FeishuChannelSettings.TryParse(_channel.SettingsJson) ?? new();
    }

    public void Dispose() => _db.Dispose();

    // ─── 基础幂等性 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_FirstCall_AddsMessageIdToSeenSet()
    {
        await _processor.ProcessMessageAsync(
            "Hello", "user1", "chat1", "msg-001", _channel, _settings);

        _seenIds.Should().ContainKey("msg-001");
    }

    [Fact]
    public async Task ProcessMessage_SameMessageIdTwice_DictionaryHasOnlyOneEntry()
    {
        await _processor.ProcessMessageAsync(
            "Hello", "user1", "chat1", "msg-dup", _channel, _settings);
        await _processor.ProcessMessageAsync(
            "Hello again", "user1", "chat1", "msg-dup", _channel, _settings);

        _seenIds.Should().HaveCount(1);
        _seenIds.Should().ContainKey("msg-dup");
    }

    [Fact]
    public async Task ProcessMessage_SameMessageIdTwice_TimestampFromFirstCallPreserved()
    {
        // 第一次调用记录时间戳
        await _processor.ProcessMessageAsync(
            "First", "user1", "chat1", "msg-ts", _channel, _settings);
        DateTimeOffset firstTimestamp = _seenIds["msg-ts"];

        await Task.Delay(50); // 确保时间推进

        // 第二次调用（去重命中），时间戳不应改变
        await _processor.ProcessMessageAsync(
            "Second", "user1", "chat1", "msg-ts", _channel, _settings);

        _seenIds["msg-ts"].Should().Be(firstTimestamp);
    }

    // ─── 不同 MessageId 均被记录 ─────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_DifferentMessageIds_BothAddedToSeenSet()
    {
        await _processor.ProcessMessageAsync(
            "Hello", "user1", "chat1", "msg-aaa", _channel, _settings);
        await _processor.ProcessMessageAsync(
            "World", "user1", "chat1", "msg-bbb", _channel, _settings);

        _seenIds.Should().ContainKey("msg-aaa");
        _seenIds.Should().ContainKey("msg-bbb");
        _seenIds.Should().HaveCount(2);
    }

    // ─── 惰性清理：超窗口旧条目在下次调用时被移除 ───────────────────────

    [Fact]
    public async Task ProcessMessage_StaleEntry_IsCleanedUpOnNextCall()
    {
        // 手动注入一条超过 5 分钟（去重窗口）的旧记录
        _seenIds["stale-old-msg"] = DateTimeOffset.UtcNow.AddMinutes(-6);

        // 处理新消息 → 触发惰性清理
        await _processor.ProcessMessageAsync(
            "New message", "user1", "chat1", "fresh-msg-001", _channel, _settings);

        // 旧条目应被清理
        _seenIds.Should().NotContainKey("stale-old-msg");
        // 新消息应被记录
        _seenIds.Should().ContainKey("fresh-msg-001");
    }

    [Fact]
    public async Task ProcessMessage_EntryJustWithinWindow_IsNotCleanedUp()
    {
        // 注入一条恰好在 5 分钟以内的记录（不应被清理）
        _seenIds["recent-msg"] = DateTimeOffset.UtcNow.AddMinutes(-4);

        await _processor.ProcessMessageAsync(
            "Trigger", "user1", "chat1", "trigger-msg", _channel, _settings);

        // 4 分钟前的记录不应被移除（窗口为 5 分钟）
        _seenIds.Should().ContainKey("recent-msg");
    }

    [Fact]
    public async Task ProcessMessage_StaleEntry_NewCallWithSameIdAllowedAfterExpiry()
    {
        // 模拟：同一条消息 6 分钟前已处理（记录在字典中但已过期）
        _seenIds["redelivered-msg"] = DateTimeOffset.UtcNow.AddMinutes(-6);

        // 触发清理（任意新消息）
        await _processor.ProcessMessageAsync(
            "Cleanup trigger", "user1", "chat1", "temp-trigger-99", _channel, _settings);

        // 旧条目被清理后，再次以 "redelivered-msg" 调用，应被正常写入（不再被视为重复）
        await _processor.ProcessMessageAsync(
            "Re-deliver", "user1", "chat1", "redelivered-msg", _channel, _settings);

        _seenIds.Should().ContainKey("redelivered-msg");
        // 时间戳应为本次调用的时间（非原来的 -6min）
        _seenIds["redelivered-msg"].Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
