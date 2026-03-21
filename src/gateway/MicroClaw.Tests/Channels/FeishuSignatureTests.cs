using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MicroClaw.Channels.Feishu;

namespace MicroClaw.Tests.Channels;

public sealed class FeishuSignatureTests
{
    private const string EncryptKey = "test-encrypt-key";
    private const string Body = """{"event":"test"}""";

    private static string ComputeExpectedSignature(string timestamp, string nonce, string encryptKey, string body)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(timestamp + nonce + encryptKey + body));
        return Convert.ToHexStringLower(hash);
    }

    // ─── VerifyWebhookSignature ───

    [Fact]
    public void VerifyWebhookSignature_CorrectSignature_ReturnsTrue()
    {
        string timestamp = "1700000000";
        string nonce = "abc123";
        string signature = ComputeExpectedSignature(timestamp, nonce, EncryptKey, Body);

        FeishuChannel.VerifyWebhookSignature(timestamp, nonce, EncryptKey, Body, signature)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_WrongSignature_ReturnsFalse()
    {
        string timestamp = "1700000000";
        string nonce = "abc123";

        FeishuChannel.VerifyWebhookSignature(timestamp, nonce, EncryptKey, Body, "deadbeef")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_TamperedBody_ReturnsFalse()
    {
        string timestamp = "1700000000";
        string nonce = "abc123";
        string signature = ComputeExpectedSignature(timestamp, nonce, EncryptKey, Body);

        FeishuChannel.VerifyWebhookSignature(timestamp, nonce, EncryptKey, "tampered", signature)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "nonce", "sig")]
    [InlineData("ts", null, "sig")]
    [InlineData("ts", "nonce", null)]
    [InlineData("", "nonce", "sig")]
    [InlineData("ts", "", "sig")]
    [InlineData("ts", "nonce", "")]
    public void VerifyWebhookSignature_NullOrEmptyParams_ReturnsFalse(
        string? timestamp, string? nonce, string? signature)
    {
        FeishuChannel.VerifyWebhookSignature(timestamp, nonce, EncryptKey, Body, signature)
            .Should().BeFalse();
    }

    // ─── IsTimestampFresh ───

    [Fact]
    public void IsTimestampFresh_RecentTimestamp_ReturnsTrue()
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        FeishuChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTimestampFresh_ExpiredTimestamp_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();

        FeishuChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Fact]
    public void IsTimestampFresh_FutureTimestampBeyondTolerance_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();

        FeishuChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void IsTimestampFresh_InvalidFormat_ReturnsFalse(string? timestamp)
    {
        FeishuChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }
}
