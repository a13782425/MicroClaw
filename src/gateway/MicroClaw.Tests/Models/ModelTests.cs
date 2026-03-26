using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Providers;

namespace MicroClaw.Tests.Models;

public sealed class ProviderConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new ProviderConfig();

        config.Id.Should().BeEmpty();
        config.DisplayName.Should().BeEmpty();
        config.Protocol.Should().Be(ProviderProtocol.OpenAI);
        config.BaseUrl.Should().BeNull();
        config.ApiKey.Should().BeEmpty();
        config.ModelName.Should().BeEmpty();
        config.IsEnabled.Should().BeTrue();
        config.Capabilities.Should().NotBeNull();
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        var original = new ProviderConfig
        {
            Id = "test-1",
            DisplayName = "Original",
            ApiKey = "sk-key"
        };

        var updated = original with { DisplayName = "Updated" };

        updated.DisplayName.Should().Be("Updated");
        updated.Id.Should().Be("test-1");
        updated.ApiKey.Should().Be("sk-key");
        original.DisplayName.Should().Be("Original");
    }

    [Fact]
    public void ProviderCapabilities_DefaultValues()
    {
        var cap = new ProviderCapabilities();

        cap.InputText.Should().BeTrue();
        cap.OutputText.Should().BeTrue();
        cap.InputImage.Should().BeFalse();
        cap.InputAudio.Should().BeFalse();
        cap.InputVideo.Should().BeFalse();
        cap.InputFile.Should().BeFalse();
        cap.OutputImage.Should().BeFalse();
        cap.OutputAudio.Should().BeFalse();
        cap.OutputVideo.Should().BeFalse();
        cap.SupportsFunctionCalling.Should().BeFalse();
        cap.SupportsResponsesApi.Should().BeFalse();
        cap.InputPricePerMToken.Should().BeNull();
        cap.OutputPricePerMToken.Should().BeNull();
    }

    [Fact]
    public void ProviderProtocol_HasExpectedValues()
    {
        Enum.GetValues<ProviderProtocol>().Should().HaveCount(2);
        Enum.IsDefined(ProviderProtocol.OpenAI).Should().BeTrue();
        Enum.IsDefined(ProviderProtocol.Anthropic).Should().BeTrue();
    }
}

public sealed class ChannelConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new ChannelConfig();

        config.Id.Should().BeEmpty();
        config.DisplayName.Should().BeEmpty();
        config.ChannelType.Should().Be(ChannelType.Web);
        config.IsEnabled.Should().BeTrue();
        config.SettingsJson.Should().Be("{}");
    }

    [Fact]
    public void ChannelType_HasExpectedValues()
    {
        Enum.GetValues<ChannelType>().Should().HaveCount(4);
        Enum.IsDefined(ChannelType.Web).Should().BeTrue();
        Enum.IsDefined(ChannelType.Feishu).Should().BeTrue();
        Enum.IsDefined(ChannelType.WeCom).Should().BeTrue();
        Enum.IsDefined(ChannelType.WeChat).Should().BeTrue();
    }

    [Fact]
    public void FeishuChannelSettings_DefaultValues()
    {
        var settings = new FeishuChannelSettings();

        settings.AppId.Should().BeEmpty();
        settings.AppSecret.Should().BeEmpty();
        settings.EncryptKey.Should().BeEmpty();
        settings.VerificationToken.Should().BeEmpty();
        settings.ConnectionMode.Should().Be("websocket");
    }

    [Fact]
    public void FeishuChannelSettings_JsonRoundTrip()
    {
        var original = new FeishuChannelSettings
        {
            AppId = "cli_test",
            AppSecret = "secret123",
            EncryptKey = "enc-key",
            VerificationToken = "verify",
            ConnectionMode = "webhook"
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<FeishuChannelSettings>(json);

        deserialized.Should().NotBeNull();
        deserialized!.AppId.Should().Be("cli_test");
        deserialized.AppSecret.Should().Be("secret123");
        deserialized.EncryptKey.Should().Be("enc-key");
        deserialized.VerificationToken.Should().Be("verify");
        deserialized.ConnectionMode.Should().Be("webhook");
    }

    [Fact]
    public void FeishuChannelSettings_TryParse_ValidJson_ReturnsSettings()
    {
        string json = """{"appId":"test","appSecret":"secret"}""";

        var settings = FeishuChannelSettings.TryParse(json);

        settings.Should().NotBeNull();
        settings!.AppId.Should().Be("test");
        settings.AppSecret.Should().Be("secret");
    }

    [Fact]
    public void FeishuChannelSettings_TryParse_InvalidJson_ReturnsNull()
    {
        FeishuChannelSettings.TryParse("not-json").Should().BeNull();
    }

    [Fact]
    public void FeishuChannelSettings_TryParse_NullOrEmpty_ReturnsNull()
    {
        FeishuChannelSettings.TryParse(null).Should().BeNull();
        FeishuChannelSettings.TryParse("").Should().BeNull();
        FeishuChannelSettings.TryParse("  ").Should().BeNull();
    }
}
