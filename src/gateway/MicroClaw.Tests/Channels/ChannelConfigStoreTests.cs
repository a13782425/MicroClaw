using System.Text.Json;
using FluentAssertions;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Channels;

public sealed class ChannelConfigStoreTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly ChannelConfigStore _store;

    public ChannelConfigStoreTests()
    {
        _store = new ChannelConfigStore(_db.CreateFactory());
    }

    public void Dispose() => _db.Dispose();

    private static ChannelConfig CreateFeishuConfig(
        string displayName = "Test Feishu",
        string appSecret = "my-secret-key-12345678") =>
        new()
        {
            DisplayName = displayName,
            ChannelType = ChannelType.Feishu,
            ProviderId = "provider-1",
            IsEnabled = true,
            SettingsJson = JsonSerializer.Serialize(new FeishuChannelSettings
            {
                AppId = "cli_test123",
                AppSecret = appSecret,
                EncryptKey = "encrypt-key",
                VerificationToken = "verify-token",
                ConnectionMode = "websocket"
            })
        };

    private static ChannelConfig CreateWebConfig(string displayName = "Test Web") =>
        new()
        {
            DisplayName = displayName,
            ChannelType = ChannelType.Web,
            ProviderId = "provider-1",
            IsEnabled = true,
            SettingsJson = "{}"
        };

    // --- CRUD Tests ---

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Add_CreatesChannelWithGeneratedId()
    {
        var result = _store.Add(CreateFeishuConfig());

        result.Id.Should().NotBeNullOrWhiteSpace();
        result.DisplayName.Should().Be("Test Feishu");
        result.ChannelType.Should().Be(ChannelType.Feishu);
        result.ProviderId.Should().Be("provider-1");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Add_ThenAll_ReturnsAddedChannel()
    {
        var added = _store.Add(CreateFeishuConfig());

        _store.All.Should().ContainSingle()
            .Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public void GetById_ExistingChannel_ReturnsIt()
    {
        var added = _store.Add(CreateFeishuConfig());

        var result = _store.GetById(added.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(added.Id);
        result.DisplayName.Should().Be("Test Feishu");
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        _store.GetById("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetByType_ReturnsMatchingChannels()
    {
        _store.Add(CreateFeishuConfig("Feishu 1"));
        _store.Add(CreateFeishuConfig("Feishu 2"));
        _store.Add(CreateWebConfig("Web 1"));

        var feishuChannels = _store.GetByType(ChannelType.Feishu);
        var webChannels = _store.GetByType(ChannelType.Web);

        feishuChannels.Should().HaveCount(2);
        webChannels.Should().ContainSingle();
    }

    [Fact]
    public void GetByType_NoMatch_ReturnsEmptyList()
    {
        _store.Add(CreateFeishuConfig());

        _store.GetByType(ChannelType.WeCom).Should().BeEmpty();
    }

    [Fact]
    public void Update_ExistingChannel_ReturnsUpdated()
    {
        var added = _store.Add(CreateWebConfig());

        var incoming = added with { DisplayName = "Updated Web", IsEnabled = false };
        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.DisplayName.Should().Be("Updated Web");
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_NonExistent_ReturnsNull()
    {
        _store.Update("non-existent", CreateWebConfig()).Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingChannel_ReturnsTrue()
    {
        var added = _store.Add(CreateWebConfig());

        _store.Delete(added.Id).Should().BeTrue();
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _store.Delete("non-existent").Should().BeFalse();
    }

    // --- Feishu AppSecret Masking ---

    [Fact]
    public void Update_FeishuWithMaskedSecret_PreservesOriginal()
    {
        var added = _store.Add(CreateFeishuConfig(appSecret: "supersecretkey12345678"));

        var maskedSettings = JsonSerializer.Serialize(new FeishuChannelSettings
        {
            AppId = "cli_test123",
            AppSecret = "***",
            EncryptKey = "encrypt-key",
            VerificationToken = "verify-token",
            ConnectionMode = "websocket"
        });

        var incoming = added with { SettingsJson = maskedSettings, DisplayName = "Renamed" };
        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.DisplayName.Should().Be("Renamed");

        var settings = FeishuChannelSettings.TryParse(updated.SettingsJson);
        settings.Should().NotBeNull();
        settings!.AppSecret.Should().Be("supersecretkey12345678");
    }

    [Fact]
    public void Update_FeishuWithNewSecret_UpdatesIt()
    {
        var added = _store.Add(CreateFeishuConfig(appSecret: "old-secret-key-12345678"));

        var newSettings = JsonSerializer.Serialize(new FeishuChannelSettings
        {
            AppId = "cli_test123",
            AppSecret = "brand-new-secret-key1234",
            EncryptKey = "encrypt-key",
            VerificationToken = "verify-token",
            ConnectionMode = "websocket"
        });

        var incoming = added with { SettingsJson = newSettings };
        var updated = _store.Update(added.Id, incoming);

        var settings = FeishuChannelSettings.TryParse(updated!.SettingsJson);
        settings!.AppSecret.Should().Be("brand-new-secret-key1234");
    }

    // --- MaskSecret Static Method ---

    [Theory]
    [InlineData("", "")]
    [InlineData("short", "***")]
    [InlineData("12345678", "***")]
    [InlineData("supersecretkey12345678", "supe***5678")]
    [InlineData("abcdefghij", "abcd***ghij")]
    public void MaskSecret_ProducesExpectedOutput(string input, string expected)
    {
        ChannelConfigStore.MaskSecret(input).Should().Be(expected);
    }

    // --- ChannelType Serialization ---

    [Theory]
    [InlineData(ChannelType.Web, "web")]
    [InlineData(ChannelType.Feishu, "feishu")]
    [InlineData(ChannelType.WeCom, "wecom")]
    [InlineData(ChannelType.WeChat, "wechat")]
    public void SerializeChannelType_ProducesExpectedString(ChannelType type, string expected)
    {
        ChannelConfigStore.SerializeChannelType(type).Should().Be(expected);
    }

    [Theory]
    [InlineData("web", ChannelType.Web)]
    [InlineData("feishu", ChannelType.Feishu)]
    [InlineData("wecom", ChannelType.WeCom)]
    [InlineData("wechat", ChannelType.WeChat)]
    [InlineData("FEISHU", ChannelType.Feishu)]
    [InlineData("unknown", ChannelType.Feishu)]
    public void ParseChannelType_ProducesExpectedEnum(string input, ChannelType expected)
    {
        ChannelConfigStore.ParseChannelType(input).Should().Be(expected);
    }

    // --- MaskSettingsSecrets ---

    [Fact]
    public void MaskSettingsSecrets_Feishu_MasksAppSecret()
    {
        var settingsJson = JsonSerializer.Serialize(new FeishuChannelSettings
        {
            AppId = "cli_test123",
            AppSecret = "supersecretkey12345678",
            EncryptKey = "encrypt-key"
        });

        var masked = ChannelConfigStore.MaskSettingsSecrets(settingsJson, ChannelType.Feishu);
        var settings = FeishuChannelSettings.TryParse(masked);

        settings.Should().NotBeNull();
        settings!.AppSecret.Should().Be("supe***5678");
        settings.AppId.Should().Be("cli_test123");
    }

    [Fact]
    public void MaskSettingsSecrets_NonFeishu_ReturnsOriginal()
    {
        var settingsJson = """{"key":"value"}""";

        var result = ChannelConfigStore.MaskSettingsSecrets(settingsJson, ChannelType.Web);

        result.Should().Be(settingsJson);
    }

    [Fact]
    public void MaskSettingsSecrets_NullJson_ReturnsEmptyObject()
    {
        ChannelConfigStore.MaskSettingsSecrets(null, ChannelType.Feishu).Should().Be("{}");
    }
}
