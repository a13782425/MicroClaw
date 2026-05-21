using Anthropic;
using MicroClaw.Abstractions;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers.Anthropic;

/// <summary>
/// Anthropic（Claude）协议的 Chat Provider。使用官方 SDK 配合 MEAI 适配层得到 <see cref="IChatClient"/>。
/// </summary>
public sealed class AnthropicChatModelClient : ChatModelClient
{
    public AnthropicChatModelClient(ProviderEntityConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker) { }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        var client = new AnthropicClient
        {
            ApiKey = ApiKey,
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.anthropic.com" : BaseUrl!,
        };

        return client.AsIChatClient(ModelName);
    }
}
