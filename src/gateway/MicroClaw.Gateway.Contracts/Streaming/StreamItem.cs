namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>流式 ReAct 循环中产出的事件基类。子类需实现 TypeName 和 ToSerializablePayload 以支持统一序列化。</summary>
public abstract record StreamItem
{
    /// <summary>SSE JSON 中的 type 字段值。</summary>
    public abstract string TypeName { get; }

    /// <summary>当前 turn 的消息 ID，用于分组同一轮次的流式事件。</summary>
    public string? MessageId { get; set; }

    /// <summary>消息可见性（null = All）。由事件源设置，SSE 和持久化层直接读取。</summary>
    public string? Visibility { get; set; }

    /// <summary>返回用于 JSON 序列化的匿名对象（不含 type 字段，由 Serializer 统一添加）。</summary>
    public abstract object ToSerializablePayload();
}

/// <summary>文本 token（逐块流式输出）。</summary>
public sealed record TokenItem(string Content) : StreamItem
{
    public override string TypeName => "token";
    public override object ToSerializablePayload() => new { content = Content, messageId = MessageId };
}

/// <summary>AI 发起的工具调用请求。</summary>
public sealed record ToolCallItem(
    string CallId,
    string ToolName,
    IDictionary<string, object?>? Arguments) : StreamItem
{
    public override string TypeName => "tool_call";
    public override object ToSerializablePayload() => new { callId = CallId, toolName = ToolName, arguments = Arguments, messageId = MessageId };
}

/// <summary>工具执行结果。</summary>
public sealed record ToolResultItem(
    string CallId,
    string ToolName,
    string Result,
    bool Success,
    long DurationMs) : StreamItem
{
    public override string TypeName => "tool_result";
    public override object ToSerializablePayload() => new { callId = CallId, toolName = ToolName, result = Result, success = Success, durationMs = DurationMs, messageId = MessageId };
}

/// <summary>子代理开始执行。</summary>
public sealed record SubAgentStartItem(
    string AgentId,
    string AgentName,
    string Task,
    string ChildSessionId) : StreamItem
{
    public override string TypeName => "sub_agent_start";
    public override object ToSerializablePayload() => new { agentId = AgentId, agentName = AgentName, task = Task, childSessionId = ChildSessionId, messageId = MessageId };
}

/// <summary>子代理执行完成。</summary>
public sealed record SubAgentResultItem(
    string AgentId,
    string AgentName,
    string Result,
    long DurationMs) : StreamItem
{
    public override string TypeName => "sub_agent_done";
    public override object ToSerializablePayload() => new { agentId = AgentId, agentName = AgentName, result = Result, durationMs = DurationMs, messageId = MessageId };
}

/// <summary>AI 输出的非文本内容（图片/音频等），对应 DataContent。</summary>
public sealed record DataContentItem(string MimeType, byte[] Data) : StreamItem
{
    public override string TypeName => "data_content";
    public override object ToSerializablePayload() => new { mimeType = MimeType, data = Convert.ToBase64String(Data), messageId = MessageId };
}

/// <summary>AI 模型的思考过程（对应 MEAI ThinkingContent）。</summary>
public sealed record ThinkingItem(string Content) : StreamItem
{
    public override string TypeName => "thinking";
    public override object ToSerializablePayload() => new { content = Content, messageId = MessageId };
}

// ── 工作流事件 ──────────────────────────────────────────────────────────────

/// <summary>工作流开始执行。</summary>
public sealed record WorkflowStartItem(
    string WorkflowId,
    string WorkflowName,
    string ExecutionId) : StreamItem
{
    public override string TypeName => "workflow_start";
    public override object ToSerializablePayload() => new { workflowId = WorkflowId, workflowName = WorkflowName, executionId = ExecutionId, messageId = MessageId };
}

/// <summary>工作流某个节点开始执行。</summary>
public sealed record WorkflowNodeStartItem(
    string ExecutionId,
    string NodeId,
    string NodeLabel,
    string NodeType) : StreamItem
{
    public override string TypeName => "workflow_node_start";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, nodeId = NodeId, nodeLabel = NodeLabel, nodeType = NodeType, messageId = MessageId };
}

/// <summary>工作流某个节点执行完成。</summary>
public sealed record WorkflowNodeCompleteItem(
    string ExecutionId,
    string NodeId,
    string Result,
    long DurationMs) : StreamItem
{
    public override string TypeName => "workflow_node_complete";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, nodeId = NodeId, result = Result, durationMs = DurationMs, messageId = MessageId };
}

/// <summary>工作流边（节点间控制流转移）。</summary>
public sealed record WorkflowEdgeItem(
    string ExecutionId,
    string SourceNodeId,
    string TargetNodeId,
    string? Condition) : StreamItem
{
    public override string TypeName => "workflow_edge";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, sourceNodeId = SourceNodeId, targetNodeId = TargetNodeId, condition = Condition, messageId = MessageId };
}

/// <summary>工作流全部执行完成，包含最终结果。</summary>
public sealed record WorkflowCompleteItem(
    string ExecutionId,
    string FinalResult,
    long TotalDurationMs) : StreamItem
{
    public override string TypeName => "workflow_complete";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, finalResult = FinalResult, totalDurationMs = TotalDurationMs, messageId = MessageId };
}

/// <summary>工作流执行错误。</summary>
public sealed record WorkflowErrorItem(
    string ExecutionId,
    string NodeId,
    string Error) : StreamItem
{
    public override string TypeName => "workflow_error";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, nodeId = NodeId, error = Error, messageId = MessageId };
}

/// <summary>工作流执行警告（如 Tool 节点跨 Agent 工具调用）。</summary>
public sealed record WorkflowWarningItem(
    string ExecutionId,
    string NodeId,
    string Warning) : StreamItem
{
    public override string TypeName => "workflow_warning";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, nodeId = NodeId, warning = Warning, messageId = MessageId };
}

/// <summary>工作流模型切换事件。</summary>
public sealed record WorkflowModelSwitchItem(
    string ExecutionId,
    string NodeId,
    string ProviderId) : StreamItem
{
    public override string TypeName => "workflow_model_switch";
    public override object ToSerializablePayload() => new { executionId = ExecutionId, nodeId = NodeId, providerId = ProviderId, messageId = MessageId };
}
