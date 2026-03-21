using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MicroClaw.Channels.WeCom;

namespace MicroClaw.Tests.Channels;

public sealed class WeComSignatureTests
{
    private const string Token = "test-token";

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
        string timestamp = "1700000000";
        string nonce     = "xyzAbc";
        string expected  = ComputeExpectedSignature(Token, timestamp, nonce);

        WeComChannel.VerifySignature(Token, timestamp, nonce, expected)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_PlainMode_WrongSignature_ReturnsFalse()
    {
        WeComChannel.VerifySignature(Token, "1700000000", "nonce", "deadbeef")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_PlainMode_TamperedTimestamp_ReturnsFalse()
    {
        string timestamp = "1700000000";
        string nonce     = "xyzAbc";
        string expected  = ComputeExpectedSignature(Token, timestamp, nonce);

        // 用不同时间戳签名，应验证失败
        WeComChannel.VerifySignature(Token, "9999999999", nonce, expected)
            .Should().BeFalse();
    }

    // ─── VerifySignature（安全模式：4 字段含 msgEncrypt）───

    [Fact]
    public void VerifySignature_SafeMode_CorrectSignature_ReturnsTrue()
    {
        string timestamp  = "1700000000";
        string nonce      = "nonce123";
        string msgEncrypt = "encryptedPayload==";
        string expected   = ComputeExpectedSignature(Token, timestamp, nonce, msgEncrypt);

        WeComChannel.VerifySignature(Token, timestamp, nonce, expected, msgEncrypt)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_SafeMode_TamperedEncrypt_ReturnsFalse()
    {
        string timestamp  = "1700000000";
        string nonce      = "nonce123";
        string msgEncrypt = "encryptedPayload==";
        string expected   = ComputeExpectedSignature(Token, timestamp, nonce, msgEncrypt);

        WeComChannel.VerifySignature(Token, timestamp, nonce, expected, "tamperedPayload==")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_SafeMode_PlainSigDoesNotMatchSafe_ReturnsFalse()
    {
        string timestamp = "1700000000";
        string nonce     = "nonce123";
        // 用 3 字段签名，但验证时传入 msgEncrypt → 结果应失败
        string plainSig  = ComputeExpectedSignature(Token, timestamp, nonce);

        WeComChannel.VerifySignature(Token, timestamp, nonce, plainSig, "encrypt==")
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
        WeComChannel.VerifySignature(Token, timestamp, nonce, signature)
            .Should().BeFalse();
    }

    // ─── IsTimestampFresh ───

    [Fact]
    public void IsTimestampFresh_RecentTimestamp_ReturnsTrue()
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        WeComChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTimestampFresh_ExpiredTimestamp_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();

        WeComChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Fact]
    public void IsTimestampFresh_FutureTimestampBeyondTolerance_ReturnsFalse()
    {
        string timestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();

        WeComChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void IsTimestampFresh_InvalidFormat_ReturnsFalse(string? timestamp)
    {
        WeComChannel.IsTimestampFresh(timestamp, 300)
            .Should().BeFalse();
    }
}
