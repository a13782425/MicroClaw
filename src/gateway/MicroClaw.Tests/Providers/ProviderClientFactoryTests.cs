using FluentAssertions;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace MicroClaw.Tests.Providers;

public sealed class ProviderClientFactoryTests
{
    [Fact]
    public void Create_WithSupportedProtocol_DelegatesToMatchingProvider()
    {
        var mockClient = Substitute.For<IChatClient>();
        var mockProvider = Substitute.For<IModelProvider>();
        mockProvider.Supports(ProviderProtocol.OpenAI).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);

        var factory = new ProviderClientFactory([mockProvider]);
        var config = new ProviderConfig { Protocol = ProviderProtocol.OpenAI };

        var result = factory.Create(config);

        result.Should().BeSameAs(mockClient);
        mockProvider.Received(1).Create(config);
    }

    [Fact]
    public void Create_WithUnsupportedProtocol_ThrowsNotSupportedException()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(false);

        var factory = new ProviderClientFactory([mockProvider]);
        var config = new ProviderConfig { Protocol = ProviderProtocol.Anthropic };

        var act = () => factory.Create(config);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Anthropic*");
    }

    [Fact]
    public void Create_WithMultipleProviders_SelectsCorrectOne()
    {
        var openAiClient = Substitute.For<IChatClient>();
        var anthropicClient = Substitute.For<IChatClient>();

        var openAiProvider = Substitute.For<IModelProvider>();
        openAiProvider.Supports(ProviderProtocol.OpenAI).Returns(true);
        openAiProvider.Supports(ProviderProtocol.Anthropic).Returns(false);
        openAiProvider.Create(Arg.Any<ProviderConfig>()).Returns(openAiClient);

        var anthropicProvider = Substitute.For<IModelProvider>();
        anthropicProvider.Supports(ProviderProtocol.OpenAI).Returns(false);
        anthropicProvider.Supports(ProviderProtocol.Anthropic).Returns(true);
        anthropicProvider.Create(Arg.Any<ProviderConfig>()).Returns(anthropicClient);

        var factory = new ProviderClientFactory([openAiProvider, anthropicProvider]);

        var resultOpenAi = factory.Create(new ProviderConfig { Protocol = ProviderProtocol.OpenAI });
        var resultAnthropic = factory.Create(new ProviderConfig { Protocol = ProviderProtocol.Anthropic });

        resultOpenAi.Should().BeSameAs(openAiClient);
        resultAnthropic.Should().BeSameAs(anthropicClient);
    }

    [Fact]
    public void Create_WithNoProviders_ThrowsNotSupportedException()
    {
        var factory = new ProviderClientFactory([]);
        var config = new ProviderConfig { Protocol = ProviderProtocol.OpenAI };

        var act = () => factory.Create(config);

        act.Should().Throw<NotSupportedException>();
    }
}
