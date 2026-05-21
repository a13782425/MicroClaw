namespace MicroClaw.Providers;

/// <summary>
/// 模型功能能力位标志。
/// </summary>
[Flags]
public enum ModelCapability
{
    None = 0,

    /// <summary>支持函数 / 工具调用。</summary>
    ToolCalling = 1 << 0,

    /// <summary>支持 OpenAI Responses API（仅在 ApiKind=OpenAI 且未自定义 BaseUrl 时生效）。</summary>
    ResponsesApi = 1 << 1,
}
