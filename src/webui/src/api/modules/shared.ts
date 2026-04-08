export type ChannelType = 'web' | 'feishu' | 'wecom' | 'wechat'

export type SseChunk =
  | { type: 'token'; content: string; messageId?: string }
  | { type: 'done'; thinkContent?: string | null; messageId?: string }
  | { type: 'error'; message: string }
  | { type: 'tool_call'; callId: string; toolName: string; arguments: Record<string, unknown>; messageId?: string }
  | { type: 'tool_result'; callId: string; toolName: string; result: string; success: boolean; durationMs: number; messageId?: string }
  | { type: 'sub_agent_start'; agentId: string; agentName: string; task: string; runId: string; messageId?: string }
  | { type: 'sub_agent_done'; agentId: string; agentName: string; result: string; durationMs: number; runId: string; messageId?: string }
  | { type: 'sub_agent_progress'; agentId: string; step: string; runId: string; messageId?: string }
  | { type: 'data_content'; mimeType: string; data: string; messageId?: string }
  | { type: 'workflow_start'; workflowId: string; workflowName: string; executionId: string }
  | { type: 'workflow_node_start'; executionId: string; nodeId: string; nodeLabel: string; nodeType: string }
  | { type: 'workflow_node_complete'; executionId: string; nodeId: string; result: string; durationMs: number }
  | { type: 'workflow_edge'; executionId: string; sourceNodeId: string; targetNodeId: string; condition?: string }
  | { type: 'workflow_complete'; executionId: string; finalResult: string; totalDurationMs: number }
  | { type: 'workflow_error'; executionId: string; nodeId: string; error: string }
  | { type: 'workflow_warning'; executionId: string; nodeId: string; warning: string }
  | { type: 'workflow_model_switch'; executionId: string; nodeId: string; providerId: string }