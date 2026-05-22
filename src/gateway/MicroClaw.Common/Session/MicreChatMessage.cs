using System.Text.Json;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;
using static MicroClaw.Common.MicroChatMessageVisibility;


namespace MicroClaw.Common;
/// <summary>
/// 一条聊天消息，包含消息内容、角色、时间戳等信息
/// </summary>
public sealed class MicroChatMessage
{
    /// <summary>消息唯一标识符。</summary>
    public string Id { get; set; } = "";
    /// <summary>消息角色，如 <c>user</c>、<c>assistant</c>、<c>tool</c>。</summary>
    public MicroChatMessageRole Role { get; set; } = MicroChatMessageRole.User;
    /// <summary>消息正文内容。</summary>
    public string Content { get; set; } = "";
    /// <summary>模型思考过程内容（thinking / reasoning token），前端可选展示。</summary>
    public string ThinkContent { get; set; } = "";
    /// <summary>
    /// 元数据，格式为 JSON 字符串，按 <see cref="MicroChatMessageType"/> 约定字段：
    /// <list type="bullet">
    ///   <item><see cref="MicroChatMessageType.ToolCall"/> —
    ///     <c>{"callId":"...","toolName":"...","arguments":{...}}</c></item>
    ///   <item><see cref="MicroChatMessageType.ToolResult"/> —
    ///     <c>{"callId":"..."}</c>，结果文本存于 <see cref="Content"/>。</item>
    ///   <item><see cref="MicroChatMessageType.Text"/> — 可选，存储附加属性（如 RAG 来源等）。</item>
    /// </list>
    /// </summary>
    public string Metadata { get; set; } = "";
    /// <summary>
    /// 消息附件列表，支持携带文件等二进制数据。每个附件包含文件名、MIME 类型和 Base64 编码的数据内容。
    /// </summary>
    public List<MessageAttachment> Attachments { get; set; } = new List<MessageAttachment>();
    
    /// <summary>消息来源入口，默认为 <see cref="MicroChatMessageSource.Chat"/>。</summary>
    public MicroChatMessageSource Source { get; set; } = MicroChatMessageSource.Chat;
    /// <summary>消息类型，标识在 ReAct 循环中的角色，默认为 <see cref="MicroChatMessageType.Text"/>。</summary>
    public MicroChatMessageType MessageType { get; set; } = MicroChatMessageType.Text;
    /// <summary>消息可见性，控制对前端和 LLM 的暴露范围，默认为 <see cref="MicroChatMessageVisibility.All"/>。</summary>
    public MicroChatMessageVisibility Visibility { get; set; } = MicroChatMessageVisibility.All;
    /// <summary>消息产生的时间戳。</summary>
    public DateTimeOffset Timestamp { get; set; }
    
}
public sealed record MessageAttachment(string FileName, string MimeType, string Base64Data);
/// <summary>
/// 消息角色。标识消息的发送者或承担的角色，影响消息在
/// 前端的展示样式和在对话中的作用。常见角色包括：<c>user</c>（用户输入）、<c>assistant</c>（模型回复）、<c>system</c>（系统通知）和 <c>tool</c>（工具调用）。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MicroChatMessageRole
{
    [JsonStringEnumMemberName("user")]
    User,
    [JsonStringEnumMemberName("assistant")]
    Assistant,
    [JsonStringEnumMemberName("system")]
    System,
    [JsonStringEnumMemberName("tool")]
    Tool
}
/// <summary>
/// 消息来源。标识该消息是由哪个入口产生的。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MicroChatMessageSource
{
    /// <summary>来自普通聊天对话。</summary>
    [JsonStringEnumMemberName("chat")]
    Chat,
}
/// <summary>
/// 消息类型。标识消息在 ReAct 循环中的角色，影响前端渲染方式和消息组装逻辑。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MicroChatMessageType
{
    /// <summary>
    /// 普通文本消息，包含用户输入和模型回复等。前端默认使用此类型展示消息内容。
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,
    /// <summary>工具调用请求，由模型发起，包含函数名和参数。</summary>
    [JsonStringEnumMemberName("tool_call")]
    ToolCall,
    /// <summary>工具调用结果，由工具执行后写回，包含返回值或错误信息。</summary>
    [JsonStringEnumMemberName("tool_result")]
    ToolResult,
}
/// <summary>
/// 消息可见性常量。控制消息对前端和 LLM 的可见范围。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MicroChatMessageVisibility
{
    /// <summary>
    /// 前端和 LLM 均可见（默认）。
    /// </summary>
    [JsonStringEnumMemberName("all")]
    All,
    /// <summary>
    /// 仅内部使用，前端和 LLM 均不可见（如后台记忆汇总）。
    /// </summary>
    [JsonStringEnumMemberName("internal")]
    Internal,
    /// <summary>
    /// 仅前端可见，不发送给 LLM（如系统通知）。
    /// </summary>
    [JsonStringEnumMemberName("client_only")]
    ClientOnly,
    /// <summary>
    /// 仅 LLM 可见，不显示给前端（如 RAG 注入）。
    /// </summary>
    [JsonStringEnumMemberName("llm_only")]
    LlmOnly
}
/// <summary>
/// <see cref="MicroChatMessageVisibility"/> 的扩展方法。
/// </summary>
public static class MicroChatMessageExtensions
{
    /// <summary>
    /// 判断消息对 LLM 是否可见（<see cref="MicroChatMessageVisibility.All"/> 或 <see cref="MicroChatMessageVisibility.LlmOnly"/>）。
    /// </summary>
    public static bool IsVisibleToLlm(this MicroChatMessageVisibility visibility) => visibility is All or LlmOnly;
    
    /// <summary>
    /// 判断消息对前端是否可见（<see cref="MicroChatMessageVisibility.All"/> 或 <see cref="MicroChatMessageVisibility.ClientOnly"/>）。
    /// </summary>
    public static bool IsVisibleToClient(this MicroChatMessageVisibility visibility) => visibility is All or ClientOnly;
    
    /// <summary>
    /// 将当前消息转换为 <see cref="ChatMessage"/>（Microsoft.Extensions.AI）。
    /// <list type="bullet">
    ///   <item><see cref="MicroChatMessageType.Text"/> — 构建 <see cref="TextContent"/> 列表，
    ///     若存在 <see cref="MicroChatMessage.ThinkContent"/> 则作为首个内容项附加。</item>
    ///   <item><see cref="MicroChatMessageType.ToolCall"/> / <see cref="MicroChatMessageType.ToolResult"/> —
    ///     因缺少 callId / toolName / arguments 等结构化字段，无法构建
    ///     <see cref="FunctionCallContent"/> / <see cref="FunctionResultContent"/>，会抛出
    ///     <see cref="NotSupportedException"/>。如需完整还原，请使用携带 Metadata 的
    ///     <c>SessionMessage</c> 进行转换。</item>
    /// </list>
    /// </summary>
    public static ChatMessage ToChatMessage(this MicroChatMessage message)
    {
        ChatRole role = message.Role switch
        {
            MicroChatMessageRole.User => ChatRole.User,
            MicroChatMessageRole.Assistant => ChatRole.Assistant,
            MicroChatMessageRole.System => ChatRole.System,
            MicroChatMessageRole.Tool => ChatRole.Tool,
            _ => throw new InvalidOperationException($"Unsupported message role: {message.Role}"),
        };
        
        List<AIContent> contents = message.MessageType switch
        {
            MicroChatMessageType.Text => BuildTextContents(message),
            MicroChatMessageType.ToolCall => BuildToolCallContents(message),
            MicroChatMessageType.ToolResult => BuildToolResultContents(message),
            _ => [new TextContent(message.Content)],
        };
        
        return new ChatMessage(role, contents) { MessageId = message.Id };
    }
    
    private static List<AIContent> BuildTextContents(MicroChatMessage message)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(message.ThinkContent))
            contents.Add(new TextContent(message.ThinkContent) { AdditionalProperties = new AdditionalPropertiesDictionary { ["think"] = true } });
        if (!string.IsNullOrEmpty(message.Content))
            contents.Add(new TextContent(message.Content));
        if (message.Attachments is { Count: > 0 })
            foreach (var att in message.Attachments)
                contents.Add(new DataContent(Convert.FromBase64String(att.Base64Data), att.MimeType));
        return contents;
    }
    private static List<AIContent> BuildToolCallContents(MicroChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Metadata))
            throw new InvalidOperationException("ToolCall message requires non-empty Metadata.");
        
        var meta = JsonSerializer.Deserialize<ToolCallMetadata>(message.Metadata);
        
        if (string.IsNullOrEmpty(meta.CallId) || string.IsNullOrEmpty(meta.ToolName))
            throw new InvalidOperationException("ToolCall Metadata must contain 'callId' and 'toolName'.");
        
        return [new FunctionCallContent(meta.CallId, meta.ToolName, meta.Arguments)];
    }
    
    private static List<AIContent> BuildToolResultContents(MicroChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Metadata))
            throw new InvalidOperationException("ToolResult message requires non-empty Metadata.");
        
        var meta = JsonSerializer.Deserialize<ToolResultMetadata>(message.Metadata);
        if (string.IsNullOrEmpty(meta.CallId))
            throw new InvalidOperationException("ToolResult Metadata must contain 'callId'.");
        
        return [new FunctionResultContent(meta.CallId, message.Content)];
    }
    private record struct ToolCallMetadata(
        [property: JsonPropertyName("callId")] string CallId,
        [property: JsonPropertyName("toolName")]
        string ToolName,
        [property: JsonPropertyName("arguments")]
        Dictionary<string, object?>? Arguments);
    
    private record struct ToolResultMetadata([property: JsonPropertyName("callId")] string CallId);
}