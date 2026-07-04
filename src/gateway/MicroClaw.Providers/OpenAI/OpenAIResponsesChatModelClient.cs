using System.ClientModel;
using MicroClaw.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

#pragma warning disable OPENAI001 // ResponsesClient 仍标注为 experimental

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Chat Provider（Responses API）。对应 <see cref="ModelProviderApiKind.OpenResponses"/>。
/// Responses API 仅官方端点支持；如需自定义网关请改用 <see cref="OpenAIChatModelClient"/>（Chat Completions）。
/// </summary>
public sealed class OpenAIResponsesChatModelClient : ChatModelClient
{
    public OpenAIResponsesChatModelClient(ProviderEntityConfig config) : base(config) { }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            options.Endpoint = new Uri(BaseUrl);

        ApiKeyCredential credential = new(ApiKey);

        OpenAIClient client = new(credential, options);
        return client.GetResponsesClient().AsIChatClient(ModelName);
    }
}
