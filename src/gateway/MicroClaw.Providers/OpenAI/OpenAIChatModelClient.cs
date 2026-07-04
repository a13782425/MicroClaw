using System.ClientModel;
using MicroClaw.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Chat Provider（Chat Completions API）。适用于官方端点及多数 OpenAI 兼容网关。
/// Responses API 由 <see cref="OpenAIResponsesChatModelClient"/> 单独实现。
/// </summary>
public sealed class OpenAIChatModelClient : ChatModelClient
{
    public OpenAIChatModelClient(ProviderEntityConfig config) : base(config) { }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            options.Endpoint = new Uri(BaseUrl);

        ApiKeyCredential credential = new(ApiKey);

        return new ChatClient(ModelName, credential, options).AsIChatClient();
    }
}
