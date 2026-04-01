namespace MicroClaw.Abstractions.Sessions;

/// <summary>消息可见性常量。控制消息对前端和 LLM 的可见范围。</summary>
public static class MessageVisibility
{
    /// <summary>前端和 LLM 均可见（默认）。</summary>
    public const string All = "all";

    /// <summary>仅内部使用，前端和 LLM 均不可见（如后台记忆汇总）。</summary>
    public const string Internal = "internal";

    /// <summary>仅前端可见，不发送给 LLM（如系统通知）。</summary>
    public const string FrontendOnly = "frontend_only";

    /// <summary>仅 LLM 可见，不显示给前端（如 RAG 注入）。</summary>
    public const string LlmOnly = "llm_only";

    /// <summary>判断消息对 LLM 是否可见。</summary>
    public static bool IsVisibleToLlm(string? visibility) =>
        visibility is null or All or LlmOnly;

    /// <summary>判断消息对前端是否可见。</summary>
    public static bool IsVisibleToFrontend(string? visibility) =>
        visibility is null or All or FrontendOnly;
}
