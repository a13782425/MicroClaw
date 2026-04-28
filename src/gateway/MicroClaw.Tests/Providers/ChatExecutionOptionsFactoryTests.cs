using FluentAssertions;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tests.Providers;

public sealed class ChatExecutionOptionsFactoryTests
{
    [Fact]
    public void Build_WithFunctionCallingProvider_AttachesToolsAndOverrides()
    {
        ProviderConfig provider = CreateProvider(ProviderFeature.FunctionCalling);
        IReadOnlyList<AITool> tools =
        [
            AIFunctionFactory.Create(() => "ok", name: "tool-1", description: "test tool")
        ];

        ChatOptions options = ChatExecutionOptionsFactory.Build(
            tools,
            provider,
            modelOverride: "override-model",
            effortOverride: "high",
            temperatureOverride: 0.42f,
            topPOverride: 0.73f);

        options.ModelId.Should().Be("override-model");
        options.MaxOutputTokens.Should().Be(provider.MaxOutputTokens);
        options.Temperature.Should().Be(0.42f);
        options.TopP.Should().Be(0.73f);
        options.Tools.Should().NotBeNull();
        options.Tools.Should().ContainSingle(tool => tool.Name == "tool-1");
        options.ToolMode.Should().Be(ChatToolMode.Auto);
        options.AllowMultipleToolCalls.Should().BeTrue();
        options.AdditionalProperties.Should().ContainKey("thinking_effort");
        options.AdditionalProperties!["thinking_effort"].Should().Be("high");
    }

    [Fact]
    public void Build_WithoutFunctionCallingSupport_DoesNotAttachTools()
    {
        ProviderConfig provider = CreateProvider(ProviderFeature.None);
        IReadOnlyList<AITool> tools =
        [
            AIFunctionFactory.Create(() => "ok", name: "tool-1", description: "test tool")
        ];

        ChatOptions options = ChatExecutionOptionsFactory.Build(tools, provider);

        options.Tools.Should().BeNull();
        options.ModelId.Should().Be(provider.ModelName);
        options.ToolMode.Should().Be(ChatToolMode.Auto);
        options.AllowMultipleToolCalls.Should().BeTrue();
    }

    private static ProviderConfig CreateProvider(ProviderFeature features) => new()
    {
        Id = "provider-1",
        DisplayName = "Provider 1",
        ModelType = ModelType.Chat,
        ModelName = "model-1",
        MaxOutputTokens = 2048,
        Capabilities = new ProviderCapabilities
        {
            Features = features,
        },
    };
}
