namespace MicroClaw.Providers;

/// <summary>
/// 模型提供方 API 协议族。
/// </summary>
public enum ModelProviderApiKind
{
    /// <summary>OpenAI 官方 / OpenAI 兼容协议（Chat Completions / Responses API）。</summary>
    OpenAI = 0,

    /// <summary>Anthropic 官方协议（Claude）。</summary>
    Anthropic = 1,

    /// <summary>其他 OpenAI 兼容供应商（必须自定义 BaseUrl，按 Chat Completions 调用）。</summary>
    Other = 2,
}
