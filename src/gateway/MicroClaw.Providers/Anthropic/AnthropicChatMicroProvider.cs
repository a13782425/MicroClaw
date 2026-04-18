using Anthropic;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers.Claude;

/// <summary>
/// Anthropic（Claude）协议的 Chat Provider。使用官方 C# SDK 搭配 MEAI
/// <see cref="IChatClient"/> 适配层，Tool-call 循环和 usage 追踪由
/// <see cref="ChatMicroProvider"/> 基类统一处理。
/// </summary>
public sealed class AnthropicChatMicroProvider : ChatMicroProvider
{
    /// <summary>通过 <see cref="ProviderConfig"/> 构造 Anthropic Chat Provider。</summary>
    public AnthropicChatMicroProvider(ProviderConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker)
    {
    }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        var client = new AnthropicClient
        {
            ApiKey = Config.ApiKey,
            BaseUrl = string.IsNullOrWhiteSpace(Config.BaseUrl)
                ? "https://api.anthropic.com"
                : Config.BaseUrl.TrimEnd('/'),
        };

        return client.AsIChatClient(Config.ModelName);
    }
}
