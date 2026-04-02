using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.Models;
using MicroClaw.Endpoints;
using MicroClaw.Abstractions;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MicroClaw.Tests.Channels;

/// <summary>
/// F-H-4：飞书 Webhook 全链路集成测试。
/// 测试层面：HTTP 路由 → 签名验证 → 渠道分发 → 响应内容。
/// AI / 会话层由 NSubstitute mock IChannel 替代，专注于端点本身的正确性。
/// </summary>
public sealed class FeishuWebhookIntegrationTests : IDisposable
{
    private const string TestEncryptKey = "test-encrypt-key-12345";
    private const string TestBody       = """{"event":"test_message"}""";

    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose() => _tempDir.Dispose();

    // ─── 辅助工厂方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 创建已注册单个飞书渠道配置的 ChannelConfigStore，返回 (store, 生成的渠道 ID)。
    /// </summary>
    private (ChannelConfigStore Store, string ChannelId) CreateStoreWithFeishu(
        string? encryptKey = null,
        bool enabled = true)
    {
        var store = new ChannelConfigStore(_tempDir.Path);
        var settings = new FeishuChannelSettings
        {
            AppId     = "cli_test_app",
            AppSecret = "app-secret-xyz",
            EncryptKey = encryptKey ?? string.Empty,
        };
        ChannelConfig added = store.Add(new ChannelConfig
        {
            DisplayName  = "Test Feishu",
            ChannelType  = ChannelType.Feishu,
            IsEnabled    = enabled,
            SettingsJson = JsonSerializer.Serialize(settings),
        });
        return (store, added.Id);
    }

    /// <summary>
    /// 创建 TestServer，注册路由、日志、ChannelConfigStore 以及可选的 IChannel。
    /// </summary>
    private TestServer CreateServer(ChannelConfigStore store, IChannel? channel = null)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddSingleton(store);
                if (channel is not null)
                    services.AddSingleton<IChannel>(channel);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapChannelWebhookEndpoints());
            });

        return new TestServer(builder);
    }

    /// <summary>
    /// 构造飞书 Webhook 请求签名（SHA256(timestamp+nonce+encryptKey+body)）。
    /// </summary>
    private static string ComputeSignature(string timestamp, string nonce, string encryptKey, string body)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(timestamp + nonce + encryptKey + body));
        return Convert.ToHexStringLower(hash);
    }

    private static string FreshTimestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    // ─── 路由与渠道查找 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_ChannelNotFound_Returns404()
    {
        var (store, _) = CreateStoreWithFeishu();
        using var server = CreateServer(store);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            "/channels/feishu/nonexistent-id/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_ChannelDisabled_Returns404()
    {
        var (store, channelId) = CreateStoreWithFeishu(enabled: false);
        using var server = CreateServer(store);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_NonFeishuChannel_Returns404()
    {
        // 注册一个微信渠道，但访问飞书 Webhook 端点
        var store = new ChannelConfigStore(_tempDir.Path);
        ChannelConfig wechat = store.Add(new ChannelConfig
        {
            DisplayName  = "WeChat",
            ChannelType  = ChannelType.WeChat,
            IsEnabled    = true,
            SettingsJson = "{}",
        });
        using var server = CreateServer(store);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            $"/channels/feishu/{wechat.Id}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_FeishuServiceNotRegistered_Returns503()
    {
        // 渠道配置存在，但 DI 中没有注册 IChannel → 503 Service Unavailable
        var (store, channelId) = CreateStoreWithFeishu();
        using var server = CreateServer(store, channel: null);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ─── 签名验证 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_NoEncryptKey_AcceptsWithoutSignature_Returns200()
    {
        // 未配置 EncryptKey → 跳过签名验证，直接转发给 IChannel
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: null);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);
        mockChannel.HandleWebhookAsync(Arg.Any<string>(), Arg.Any<ChannelConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_WithEncryptKey_ValidSignature_Returns200()
    {
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: TestEncryptKey);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);
        mockChannel.HandleWebhookAsync(Arg.Any<string>(), Arg.Any<ChannelConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        string timestamp = FreshTimestamp();
        string nonce     = "test-nonce-abc";
        string sig       = ComputeSignature(timestamp, nonce, TestEncryptKey, TestBody);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/channels/feishu/{channelId}/webhook")
        {
            Content = new StringContent(TestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Lark-Signature",          sig);
        request.Headers.Add("X-Lark-Request-Timestamp",  timestamp);
        request.Headers.Add("X-Lark-Request-Nonce",      nonce);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_WithEncryptKey_InvalidSignature_Returns401()
    {
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: TestEncryptKey);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/channels/feishu/{channelId}/webhook")
        {
            Content = new StringContent(TestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Lark-Signature",         "deadbeefdeadbeefdeadbeefdeadbeef00000000deadbeefdeadbeefdeadbeef");
        request.Headers.Add("X-Lark-Request-Timestamp", FreshTimestamp());
        request.Headers.Add("X-Lark-Request-Nonce",     "test-nonce");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_WithEncryptKey_ExpiredTimestamp_Returns401()
    {
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: TestEncryptKey);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        // 10 分钟前的时间戳，超出默认 300 秒容忍窗口
        string expiredTs = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        string nonce     = "test-nonce";
        string sig       = ComputeSignature(expiredTs, nonce, TestEncryptKey, TestBody);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/channels/feishu/{channelId}/webhook")
        {
            Content = new StringContent(TestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Lark-Signature",         sig);
        request.Headers.Add("X-Lark-Request-Timestamp", expiredTs);
        request.Headers.Add("X-Lark-Request-Nonce",     nonce);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_WithEncryptKey_MissingSignatureHeader_Returns401()
    {
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: TestEncryptKey);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        // 只发时间戳和 Nonce，缺少 X-Lark-Signature
        var request = new HttpRequestMessage(HttpMethod.Post, $"/channels/feishu/{channelId}/webhook")
        {
            Content = new StringContent(TestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Lark-Request-Timestamp", FreshTimestamp());
        request.Headers.Add("X-Lark-Request-Nonce",     "test-nonce");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── 消息内容处理 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_UrlVerification_ReturnsChallenge()
    {
        // IChannel.HandleWebhookAsync 返回 challenge JSON → 端点应原样输出
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: null);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);
        mockChannel.HandleWebhookAsync(Arg.Any<string>(), Arg.Any<ChannelConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(@"{""challenge"":""test-challenge-xyz""}"));

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        string urlVerifyBody = """{"type":"url_verification","challenge":"test-challenge-xyz"}""";
        var response = await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(urlVerifyBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string responseJson = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(responseJson);
        doc.RootElement.GetProperty("challenge").GetString()
            .Should().Be("test-challenge-xyz");
    }

    [Fact]
    public async Task Webhook_NullChannelResponse_Returns200()
    {
        // HandleWebhookAsync 返回 null → 端点直接 200 OK（无响应体）
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: null);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);
        mockChannel.HandleWebhookAsync(Arg.Any<string>(), Arg.Any<ChannelConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_NormalMessage_InvokesHandleWebhookAsyncOnce()
    {
        // 验证端点将正确 body 和对应 ChannelConfig 传入 IChannel
        var (store, channelId) = CreateStoreWithFeishu(encryptKey: null);

        IChannel mockChannel = Substitute.For<IChannel>();
        mockChannel.Type.Returns(ChannelType.Feishu);
        mockChannel.HandleWebhookAsync(Arg.Any<string>(), Arg.Any<ChannelConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        using var server = CreateServer(store, mockChannel);
        using var client = server.CreateClient();

        await client.PostAsync(
            $"/channels/feishu/{channelId}/webhook",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        await mockChannel.Received(1).HandleWebhookAsync(
            TestBody,
            Arg.Is<ChannelConfig>(c => c.Id == channelId && c.ChannelType == ChannelType.Feishu),
            Arg.Any<CancellationToken>());
    }
}
