using System.ClientModel;
using MicroClaw.Abstractions;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

#pragma warning disable OPENAI001 // ResponsesClient 仍标注为 experimental

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Chat Provider。支持两种传输通道：
/// <list type="bullet">
///   <item><b>Chat Completions API</b>（默认）：适用于多数 OpenAI 兼容网关。</item>
///   <item><b>Responses API</b>：当 <see cref="ModelCapability.ResponsesApi"/> 命中且未自定义 BaseUrl 时启用。</item>
/// </list>
/// </summary>
public sealed class OpenAIChatModelClient : ChatModelClient
{
    public OpenAIChatModelClient(ProviderEntityConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker) { }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            options.Endpoint = new Uri(BaseUrl);

        var credential = new ApiKeyCredential(ApiKey);

        // 自定义 BaseUrl 时必须降级为 Chat Completions（Responses API 仅官方端点支持）。
        bool useResponsesApi = Capabilities.HasFlag(ModelCapability.ResponsesApi)
            && string.IsNullOrWhiteSpace(BaseUrl);

        if (useResponsesApi)
        {
            var client = new OpenAIClient(credential, options);
            return client.GetResponsesClient().AsIChatClient(ModelName);
        }

        return new ChatClient(ModelName, credential, options).AsIChatClient();
    }
}
