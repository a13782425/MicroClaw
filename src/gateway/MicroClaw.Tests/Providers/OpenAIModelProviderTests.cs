using FluentAssertions;
using MicroClaw.Providers;
using MicroClaw.Providers.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

#pragma warning disable OPENAI001

namespace MicroClaw.Tests.Providers;

public sealed class OpenAIModelProviderTests
{
    private readonly OpenAIModelProvider _sut = new(Substitute.For<ILoggerFactory>());

    [Fact]
    public void Supports_OpenAI_ReturnsTrue()
    {
        _sut.Supports(ProviderProtocol.OpenAI).Should().BeTrue();
    }

    [Fact]
    public void Supports_Anthropic_ReturnsFalse()
    {
        _sut.Supports(ProviderProtocol.Anthropic).Should().BeFalse();
    }

    [Fact]
    public void Create_DefaultConfig_ReturnsChatClient()
    {
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            ModelName = "gpt-4o",
            Capabilities = new ProviderCapabilities { SupportsResponsesApi = false }
        };

        var client = _sut.Create(config);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void Create_WithResponsesApi_ReturnsChatClient()
    {
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            ModelName = "gpt-4o",
            Capabilities = new ProviderCapabilities { SupportsResponsesApi = true }
        };

        var client = _sut.Create(config);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void Create_WithCustomBaseUrl_DoesNotThrow()
    {
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            ModelName = "gpt-4o",
            BaseUrl = "https://custom.openai.example.com/v1"
        };

        var act = () => _sut.Create(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_DefaultAndResponsesApi_ReturnDifferentClientTypes()
    {
        var baseConfig = new ProviderConfig
        {
            ApiKey = "test-key",
            ModelName = "gpt-4o"
        };

        var chatClient = _sut.Create(baseConfig);
        var responsesClient = _sut.Create(baseConfig with
        {
            Capabilities = new ProviderCapabilities { SupportsResponsesApi = true }
        });

        // Both are IChatClient but backed by different inner implementations.
        // Verify via GetService: only the Responses path exposes OpenAI.Responses.ResponsesClient.
        var responsesInner = responsesClient.GetService<OpenAI.Responses.ResponsesClient>();
        var chatInner = chatClient.GetService<OpenAI.Responses.ResponsesClient>();

        responsesInner.Should().NotBeNull("Responses API path should expose ResponsesClient");
        chatInner.Should().BeNull("Chat Completions path should not expose ResponsesClient");
    }

    [Fact]
    public void Create_ResponsesApiWithCustomBaseUrl_FallsBackToChatCompletions()
    {
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            ModelName = "gpt-4o",
            DisplayName = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            Capabilities = new ProviderCapabilities { SupportsResponsesApi = true }
        };

        var client = _sut.Create(config);

        // Should fall back to Chat Completions path despite SupportsResponsesApi=true
        client.Should().NotBeNull();
        client.GetService<OpenAI.Responses.ResponsesClient>()
            .Should().BeNull("custom BaseUrl should force fallback to Chat Completions");
    }
}
