namespace MicroClaw.Providers;

/// <summary>
/// 模型提供方 API 协议族。
/// </summary>
public enum ModelProviderApiKind
{
    /// <summary>
    /// OpenAI 官方 （Chat Completions ）。
    /// </summary>
    OpenChat = 0,

    /// <summary>
    /// OpenAI 官方（Responses API ）。
    /// </summary>
    OpenResponses = 1,

    /// <summary>
    /// Anthropic 官方协议（Claude）。
    /// </summary>
    Anthropic = 2,
}
