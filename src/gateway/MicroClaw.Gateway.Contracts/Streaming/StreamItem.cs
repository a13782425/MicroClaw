namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>流式 ReAct 循环中产出的事件基类。</summary>
public abstract record StreamItem;

/// <summary>文本 token（逐块流式输出）。</summary>
public sealed record TokenItem(string Content) : StreamItem;

/// <summary>AI 发起的工具调用请求。</summary>
public sealed record ToolCallItem(
    string CallId,
    string ToolName,
    IDictionary<string, object?>? Arguments) : StreamItem;

/// <summary>工具执行结果。</summary>
public sealed record ToolResultItem(
    string CallId,
    string ToolName,
    string Result,
    bool Success,
    long DurationMs) : StreamItem;

/// <summary>子代理开始执行。</summary>
public sealed record SubAgentStartItem(
    string AgentId,
    string AgentName,
    string Task,
    string ChildSessionId) : StreamItem;

/// <summary>子代理执行完成。</summary>
public sealed record SubAgentResultItem(
    string AgentId,
    string AgentName,
    string Result,
    long DurationMs) : StreamItem;

/// <summary>AI 输出的非文本内容（图片/音频等），对应 DataContent。</summary>
public sealed record DataContentItem(string MimeType, byte[] Data) : StreamItem;
