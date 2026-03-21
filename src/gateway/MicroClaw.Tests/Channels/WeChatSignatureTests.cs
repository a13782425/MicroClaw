using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MicroClaw.Channels.WeChat;

namespace MicroClaw.Tests.Channels;

public sealed class WeChatSignatureTests
{
    private const string Token = "wechat-token";

    private static string ComputeExpectedSignature(params string[] parts)
    {
        string[] sorted = [.. parts];
        Array.Sort(sorted, StringComparer.Ordinal);
        string content = string.Concat(sorted);
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    // ─── VerifySignature（明文模式：3 字段）───

    [Fact]
    public void VerifySignature_PlainMode_CorrectSignature_ReturnsTrue()
    {
        string timestamp = "1700000001";
        string nonce     = "abc789";
        string expected  = ComputeExpectedSignature(Token, timestamp, nonce);

        WeChatChannel.VerifySignature(Token, timestamp, nonce, expected)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_PlainMode_WrongSignature_ReturnsFalse()
    {
        WeChatChannel.VerifySignature(Token, "1700000001", "abc789", "deadbeef")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_PlainMode_TamperedNonce_ReturnsFalse()
    {
        string timestamp = "1700000001";
        string nonce     = "abc789";
        string expected  = ComputeExpectedSignature(Token, timestamp, nonce);

        WeChatChannel.VerifySignature(Token, timestamp, "tampered", expected)
            .Should().BeFalse();
    }

    // ─── VerifySignature（安全模式：4 字段含 msgEncrypt）───

    [Fact]
    public void VerifySignature_SafeMode_CorrectSignature_ReturnsTrue()
    {
        string timestamp  = "1700000001";
        string nonce      = "abc789";
        string msgEncrypt = "encryptedBody==";
        string expected   = ComputeExpectedSignature(Token, timestamp, nonce, msgEncrypt);

        WeChatChannel.VerifySignature(Token, timestamp, nonce, expected, msgEncrypt)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_SafeMode_TamperedEncrypt_ReturnsFalse()
    {
        string timestamp  = "1700000001";
        string nonce      = "abc789";
        string msgEncrypt = "encryptedBody==";
        string expected   = ComputeExpectedSignature(Token, timestamp, nonce, msgEncrypt);

        WeChatChannel.VerifySignature(Token, timestamp, nonce, expected, "tampered==")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_SafeMode_PlainSigDoesNotMatchSafe_ReturnsFalse()
    {
        string timestamp = "1700000001";
        string nonce     = "abc789";
        // 用 3 字段生成签名，但验证时加入 msgEncrypt → 应失败
        string plainSig  = ComputeExpectedSignature(Token, timestamp, nonce);

        WeChatChannel.VerifySignature(Token, timestamp, nonce, plainSig, "someEncrypt==")
            .Should().BeFalse();
    }

    // ─── 参数校验 ───

    [Theory]
    [InlineData(null, "nonce", "sig")]
    [InlineData("ts", null, "sig")]
    [InlineData("ts", "nonce", null)]
    [InlineData("", "nonce", "sig")]
    [InlineData("ts", "", "sig")]
    [InlineData("ts", "nonce", "")]
    public void VerifySignature_NullOrEmptyParams_ReturnsFalse(
        string? timestamp, string? nonce, string? signature)
    {
        WeChatChannel.VerifySignature(Token, timestamp, nonce, signature)
            .Should().BeFalse();
    }

    // ─── IsTimestampFresh ───

    [Fact]
    public void IsTimestampFresh_RecentTimestamp_ReturnsTrue()
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        WeChatChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTimestampFresh_ExpiredTimestamp_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();

        WeChatChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Fact]
    public void IsTimestampFresh_FutureTimestampBeyondTolerance_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();

        WeChatChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void IsTimestampFresh_InvalidFormat_ReturnsFalse(string? timestamp)
    {
        WeChatChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }
}
