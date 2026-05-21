namespace MicroClaw.Providers;

/// <summary>
/// 模型用途分类：决定该 Profile 构造的 Provider 客户端类型。
/// </summary>
public enum ModelKind
{
    /// <summary>对话模型（包括函数调用 / 视觉 / Responses API 等）。</summary>
    Chat = 0,

    /// <summary>嵌入模型。</summary>
    Embedding = 1,
}
