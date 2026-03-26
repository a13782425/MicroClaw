using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>
/// 将 <see cref="StreamItem"/> 子类序列化为 SSE data 行的 JSON 字符串。
/// 集中管理所有流式事件的 JSON 字段名，确保前端协议一致。
/// </summary>
public static class StreamItemSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 保留非 ASCII 字符（中文等）不转义，使 SSE 输出更易读
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 将 <see cref="StreamItem"/> 序列化为 SSE JSON 字符串。
    /// <para>
    /// DataContentItem 的二进制 Data 将被转换为 Base64 字符串；
    /// 其他类型直接序列化对应字段。
    /// </para>
    /// </summary>
    /// <exception cref="NotSupportedException">遇到未知的 StreamItem 子类型时抛出。</exception>
    public static string Serialize(StreamItem item) => item switch
    {
        TokenItem t => JsonSerializer.Serialize(
            new { type = "token", content = t.Content }, Opts),

        ToolCallItem tc => JsonSerializer.Serialize(
            new { type = "tool_call", callId = tc.CallId, toolName = tc.ToolName, arguments = tc.Arguments }, Opts),

        ToolResultItem tr => JsonSerializer.Serialize(
            new { type = "tool_result", callId = tr.CallId, toolName = tr.ToolName, result = tr.Result, success = tr.Success, durationMs = tr.DurationMs }, Opts),

        SubAgentStartItem s => JsonSerializer.Serialize(
            new { type = "sub_agent_start", agentId = s.AgentId, agentName = s.AgentName, task = s.Task, childSessionId = s.ChildSessionId }, Opts),

        SubAgentResultItem r => JsonSerializer.Serialize(
            new { type = "sub_agent_done", agentId = r.AgentId, agentName = r.AgentName, result = r.Result, durationMs = r.DurationMs }, Opts),

        DataContentItem d => JsonSerializer.Serialize(
            new { type = "data_content", mimeType = d.MimeType, data = Convert.ToBase64String(d.Data) }, Opts),

        WorkflowStartItem ws => JsonSerializer.Serialize(
            new { type = "workflow_start", workflowId = ws.WorkflowId, workflowName = ws.WorkflowName, executionId = ws.ExecutionId }, Opts),

        WorkflowNodeStartItem wns => JsonSerializer.Serialize(
            new { type = "workflow_node_start", executionId = wns.ExecutionId, nodeId = wns.NodeId, nodeLabel = wns.NodeLabel, nodeType = wns.NodeType }, Opts),

        WorkflowNodeCompleteItem wnc => JsonSerializer.Serialize(
            new { type = "workflow_node_complete", executionId = wnc.ExecutionId, nodeId = wnc.NodeId, result = wnc.Result, durationMs = wnc.DurationMs }, Opts),

        WorkflowEdgeItem we => JsonSerializer.Serialize(
            new { type = "workflow_edge", executionId = we.ExecutionId, sourceNodeId = we.SourceNodeId, targetNodeId = we.TargetNodeId, condition = we.Condition }, Opts),

        WorkflowCompleteItem wc => JsonSerializer.Serialize(
            new { type = "workflow_complete", executionId = wc.ExecutionId, finalResult = wc.FinalResult, totalDurationMs = wc.TotalDurationMs }, Opts),

        WorkflowErrorItem wer => JsonSerializer.Serialize(
            new { type = "workflow_error", executionId = wer.ExecutionId, nodeId = wer.NodeId, error = wer.Error }, Opts),

        _ => throw new NotSupportedException($"Unknown StreamItem type: {item.GetType().Name}")
    };
}
