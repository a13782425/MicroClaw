using System.ClientModel;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

#pragma warning disable OPENAI001 // ResponsesClient 仍被标记为 experimental

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Chat Provider。支持两种传输通道：
/// <list type="bullet">
///   <item><b>Chat Completions API</b>（默认）：适用于大多数兼容 OpenAI 协议的第三方网关。</item>
///   <item>
///     <b>Responses API</b>：当 <see cref="ProviderCapabilities.SupportsResponsesApi"/> 为 <c>true</c>
///     且未自定义 <see cref="ProviderConfig.BaseUrl"/> 时启用，支持内置工具、会话状态等 Responses API 特有能力。
///   </item>
/// </list>
/// 两条路径都会产出 <see cref="IChatClient"/>，基类处理后续的消息/工具/usage 逻辑无差异。
/// </summary>
public sealed class OpenAIChatMicroProvider : ChatMicroProvider
{
    /// <summary>通过 <see cref="ProviderConfig"/> 构造 OpenAI Chat Provider。</summary>
    public OpenAIChatMicroProvider(ProviderConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker)
    {
    }

    /// <inheritdoc />
    protected override IChatClient BuildClient()
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(Config.BaseUrl))
            options.Endpoint = new Uri(Config.BaseUrl);

        var credential = new ApiKeyCredential(Config.ApiKey);

        // 自定义 BaseUrl 时必须降级为 Chat Completions（Responses API 仅对接官方端点）。
        bool useResponsesApi = Config.Capabilities.SupportsResponsesApi
            && string.IsNullOrWhiteSpace(Config.BaseUrl);

        if (useResponsesApi)
        {
            var client = new OpenAIClient(credential, options);
            return client.GetResponsesClient().AsIChatClient(Config.ModelName);
        }

        return new ChatClient(Config.ModelName, credential, options).AsIChatClient();
    }
}
