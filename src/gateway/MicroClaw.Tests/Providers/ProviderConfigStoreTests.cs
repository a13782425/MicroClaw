using FluentAssertions;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Providers;

public sealed class ProviderConfigStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _configDir = new();
    private readonly ProviderConfigStore _store;

    public ProviderConfigStoreTests()
    {
        _store = new ProviderConfigStore(_configDir.Path);
    }

    public void Dispose() => _configDir.Dispose();

    private static ProviderConfig CreateSampleConfig(
        string displayName = "Test GPT",
        ProviderProtocol protocol = ProviderProtocol.OpenAI,
        string apiKey = "sk-test-key-12345",
        string modelName = "gpt-4o") =>
        new()
        {
            DisplayName = displayName,
            Protocol = protocol,
            ApiKey = apiKey,
            ModelName = modelName,
            IsEnabled = true,
            Capabilities = new ProviderCapabilities
            {
                InputText = true,
                OutputText = true,
                SupportsFunctionCalling = true
            }
        };

    // --- CRUD Tests ---

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Add_CreatesProviderWithGeneratedId()
    {
        var config = CreateSampleConfig();

        var result = _store.Add(config);

        result.Id.Should().NotBeNullOrWhiteSpace();
        result.DisplayName.Should().Be("Test GPT");
        result.Protocol.Should().Be(ProviderProtocol.OpenAI);
        result.ModelName.Should().Be("gpt-4o");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Add_ThenAll_ReturnsAddedProvider()
    {
        var config = CreateSampleConfig();

        var added = _store.Add(config);
        var all = _store.All;

        all.Should().ContainSingle()
            .Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public void Update_ExistingProvider_ReturnsUpdated()
    {
        var added = _store.Add(CreateSampleConfig());

        var incoming = added with
        {
            DisplayName = "Updated GPT",
            ModelName = "gpt-4o-mini",
            IsEnabled = false
        };

        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.DisplayName.Should().Be("Updated GPT");
        updated.ModelName.Should().Be("gpt-4o-mini");
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_NonExistent_ReturnsNull()
    {
        var result = _store.Update("non-existent-id", CreateSampleConfig());

        result.Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingProvider_ReturnsTrue()
    {
        var added = _store.Add(CreateSampleConfig());

        var result = _store.Delete(added.Id);

        result.Should().BeTrue();
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        var result = _store.Delete("non-existent-id");

        result.Should().BeFalse();
    }

    // --- API Key Masking ---

    [Fact]
    public void Update_WithMaskedApiKey_PreservesOriginal()
    {
        var added = _store.Add(CreateSampleConfig(apiKey: "sk-real-secret-key"));

        var incoming = added with { ApiKey = "***", DisplayName = "Renamed" };
        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.DisplayName.Should().Be("Renamed");
        updated.ApiKey.Should().Be("sk-real-secret-key");
    }

    [Fact]
    public void Update_WithNewApiKey_UpdatesIt()
    {
        var added = _store.Add(CreateSampleConfig(apiKey: "sk-old-key"));

        var incoming = added with { ApiKey = "sk-new-key" };
        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.ApiKey.Should().Be("sk-new-key");
    }

    [Fact]
    public void Update_WithEmptyApiKey_PreservesOriginal()
    {
        var added = _store.Add(CreateSampleConfig(apiKey: "sk-real-key"));

        var incoming = added with { ApiKey = "" };
        var updated = _store.Update(added.Id, incoming);

        updated.Should().NotBeNull();
        updated!.ApiKey.Should().Be("sk-real-key");
    }

    // --- Protocol Serialization ---

    [Theory]
    [InlineData(ProviderProtocol.OpenAI)]
    [InlineData(ProviderProtocol.Anthropic)]
    public void Add_ThenAll_PreservesProtocol(ProviderProtocol protocol)
    {
        var config = CreateSampleConfig(protocol: protocol);

        _store.Add(config);
        var result = _store.All.Single();

        result.Protocol.Should().Be(protocol);
    }

    // --- Capabilities JSON ---

    [Fact]
    public void Add_WithCapabilities_PreservesCapabilities()
    {
        var config = CreateSampleConfig() with
        {
            Capabilities = new ProviderCapabilities
            {
                InputText = true,
                InputImage = true,
                OutputText = true,
                SupportsFunctionCalling = true,
                InputPricePerMToken = 2.5m,
                OutputPricePerMToken = 10m
            }
        };

        _store.Add(config);
        var result = _store.All.Single();

        result.Capabilities.InputText.Should().BeTrue();
        result.Capabilities.InputImage.Should().BeTrue();
        result.Capabilities.SupportsFunctionCalling.Should().BeTrue();
        result.Capabilities.InputPricePerMToken.Should().Be(2.5m);
        result.Capabilities.OutputPricePerMToken.Should().Be(10m);
    }

    [Fact]
    public void Add_WithDefaultCapabilities_ReturnsDefaults()
    {
        var config = CreateSampleConfig();

        _store.Add(config);
        var result = _store.All.Single();

        result.Capabilities.InputText.Should().BeTrue();
        result.Capabilities.OutputText.Should().BeTrue();
        result.Capabilities.InputImage.Should().BeFalse();
        result.Capabilities.SupportsFunctionCalling.Should().BeTrue();
    }

    // --- BaseUrl ---

    [Fact]
    public void Add_WithBaseUrl_PreservesBaseUrl()
    {
        var config = CreateSampleConfig() with { BaseUrl = "https://api.example.com" };

        _store.Add(config);
        var result = _store.All.Single();

        result.BaseUrl.Should().Be("https://api.example.com");
    }

    [Fact]
    public void Add_WithNullBaseUrl_PreservesNull()
    {
        var config = CreateSampleConfig() with { BaseUrl = null };

        _store.Add(config);
        var result = _store.All.Single();

        result.BaseUrl.Should().BeNull();
    }

    [Fact]
    public void Add_WithWhitespaceBaseUrl_ConvertsToNull()
    {
        var config = CreateSampleConfig() with { BaseUrl = "   " };

        _store.Add(config);
        var result = _store.All.Single();

        result.BaseUrl.Should().BeNull();
    }
}
