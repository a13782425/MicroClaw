using System.Text.Json.Serialization;
namespace MicroClaw.Common;
/// <summary>
/// 流式 ReAct 循环中产出的事件基类。子类需实现 TypeName 和 ToSerializablePayload 以支持统一序列化。
/// </summary>
public abstract class ChatStreamItem
{
    /// <summary>
    /// SSE JSON 中的 type 字段值。
    /// </summary>
    [JsonIgnore]
    public abstract string TypeName { get; }
    
    /// <summary>
    /// 当前 turn 的消息 ID，用于分组同一轮次的流式事件。
    /// </summary>
    public string? MessageId { get; set; }
    
    /// <summary>
    /// 消息可见性（null = All）。由事件源设置，SSE 和持久化层直接读取。
    /// </summary>
    [JsonIgnore]
    public string? Visibility { get; set; }
}
/// <summary>
/// 文本 token（逐块流式输出）。
/// </summary>
public sealed class ChatTokenStreamItem(string content) : ChatStreamItem
{
    public override string TypeName => "token";
    public string Content { get; init; } = content;
}