using FluentAssertions;
using Microsoft.Extensions.AI;
using MicroClaw.Providers;
using MicroClaw.RAG;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

public class EmbeddingServiceTests
{
    // ── 构造 ──

    [Fact]
    public void Ctor_NullGenerator_Throws()
    {
        var act = () => new EmbeddingService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── GenerateAsync ──

    [Fact]
    public async Task GenerateAsync_ReturnsSingleVector()
    {
        float[] expected = [0.1f, 0.2f, 0.3f];
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(expected)]));

        var sut = new EmbeddingService(generator);
        var result = await sut.GenerateAsync("hello");

        result.ToArray().Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GenerateAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1f })]));

        var sut = new EmbeddingService(generator);
        await sut.GenerateAsync("test", cts.Token);

        await generator.Received(1).GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            cts.Token);
    }

    // ── GenerateBatchAsync ──

    [Fact]
    public async Task GenerateBatchAsync_ReturnsMultipleVectors()
    {
        float[] v1 = [1f, 0f, 0f];
        float[] v2 = [0f, 1f, 0f];
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([
                new Embedding<float>(v1),
                new Embedding<float>(v2)
            ]));

        var sut = new EmbeddingService(generator);
        var results = await sut.GenerateBatchAsync(["hello", "world"]);

        results.Should().HaveCount(2);
        results[0].ToArray().Should().BeEquivalentTo(v1);
        results[1].ToArray().Should().BeEquivalentTo(v2);
    }

    [Fact]
    public async Task GenerateBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([]));

        var sut = new EmbeddingService(generator);
        var results = await sut.GenerateBatchAsync([]);

        results.Should().BeEmpty();
    }
}

public class ProviderEmbeddingFactoryTests
{
    [Fact]
    public void Create_UnsupportedProtocol_Throws()
    {
        var provider = Substitute.For<IEmbeddingProvider>();
        provider.Supports(Arg.Any<ProviderProtocol>()).Returns(false);

        var factory = new ProviderEmbeddingFactory([provider]);
        var config = new ProviderConfig { Protocol = ProviderProtocol.Anthropic };

        var act = () => factory.Create(config);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Create_MatchingProvider_ReturnsGenerator()
    {
        var mockGenerator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        var provider = Substitute.For<IEmbeddingProvider>();
        provider.Supports(ProviderProtocol.OpenAI).Returns(true);
        provider.Create(Arg.Any<ProviderConfig>()).Returns(mockGenerator);

        var factory = new ProviderEmbeddingFactory([provider]);
        var config = new ProviderConfig { Protocol = ProviderProtocol.OpenAI };

        var result = factory.Create(config);
        result.Should().BeSameAs(mockGenerator);
    }

    [Fact]
    public void Create_MultipleProviders_PicksCorrectOne()
    {
        var gen1 = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        var gen2 = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();

        var p1 = Substitute.For<IEmbeddingProvider>();
        p1.Supports(ProviderProtocol.OpenAI).Returns(true);
        p1.Supports(ProviderProtocol.Anthropic).Returns(false);
        p1.Create(Arg.Any<ProviderConfig>()).Returns(gen1);

        var p2 = Substitute.For<IEmbeddingProvider>();
        p2.Supports(ProviderProtocol.OpenAI).Returns(false);
        p2.Supports(ProviderProtocol.Anthropic).Returns(true);
        p2.Create(Arg.Any<ProviderConfig>()).Returns(gen2);

        var factory = new ProviderEmbeddingFactory([p1, p2]);

        factory.Create(new ProviderConfig { Protocol = ProviderProtocol.OpenAI }).Should().BeSameAs(gen1);
        factory.Create(new ProviderConfig { Protocol = ProviderProtocol.Anthropic }).Should().BeSameAs(gen2);
    }
}
