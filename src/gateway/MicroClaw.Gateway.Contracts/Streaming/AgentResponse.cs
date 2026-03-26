namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>Agent 执行的结构化响应，包含文本、思考内容和多模态附件。</summary>
public sealed record AgentResponse(
    string Text,
    string? ThinkContent,
    IReadOnlyList<ResponseAttachment> Attachments)
{
    public static AgentResponse Empty { get; } = new("", null, []);
}

/// <summary>AI 输出的非文本附件（图片/音频等）。</summary>
public sealed record ResponseAttachment(string MimeType, byte[] Data, string? FileName = null);
