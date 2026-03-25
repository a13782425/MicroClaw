import request from './request'
import { useAuthStore } from '@/store/authStore'

// ─── Health ───────────────────────────────────────────────────────────────────

export type GatewayHealth = {
  status: string
  service: string
  utcNow: string
  version: string
}

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await request.get<GatewayHealth>('/api/health')
  return data
}

// ─── Auth ─────────────────────────────────────────────────────────────────────

export async function login(username: string, password: string) {
  const { data } = await request.post('/api/auth/login', { username, password })
  return data as {
    token: string
    username: string
    role: string
    expiresAtUtc: string
  }
}

// ─── Providers ────────────────────────────────────────────────────────────────

export type ProviderProtocol = 'openai' | 'anthropic'

export type ProviderCapabilities = {
  inputText: boolean
  inputImage: boolean
  inputAudio: boolean
  inputVideo: boolean
  inputFile: boolean
  outputText: boolean
  outputImage: boolean
  outputAudio: boolean
  outputVideo: boolean
  supportsFunctionCalling: boolean
  supportsResponsesApi: boolean
  inputPricePerMToken: number | null
  outputPricePerMToken: number | null
  cacheInputPricePerMToken: number | null
  cacheOutputPricePerMToken: number | null
  notes: string | null
}

export type ProviderConfig = {
  id: string
  displayName: string
  protocol: ProviderProtocol
  baseUrl: string | null
  apiKey: string
  modelName: string
  maxOutputTokens: number
  isEnabled: boolean
  isDefault: boolean
  capabilities: ProviderCapabilities
}

export type ProviderCreateRequest = {
  displayName: string
  protocol: ProviderProtocol
  baseUrl?: string
  apiKey: string
  modelName: string
  maxOutputTokens?: number
  isEnabled: boolean
  capabilities?: Partial<ProviderCapabilities>
}

export type ProviderUpdateRequest = {
  id: string
  displayName?: string
  protocol?: ProviderProtocol
  baseUrl?: string
  apiKey?: string
  modelName?: string
  maxOutputTokens?: number
  isEnabled: boolean
  capabilities?: Partial<ProviderCapabilities>
}

export async function listProviders(): Promise<ProviderConfig[]> {
  const { data } = await request.get<ProviderConfig[]>('/api/providers')
  return data
}

export async function createProvider(req: ProviderCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/providers', req)
  return data
}

export async function updateProvider(req: ProviderUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/providers/update', req)
  return data
}

export async function deleteProvider(id: string): Promise<void> {
  await request.post('/api/providers/delete', { id })
}

export async function setDefaultProvider(id: string): Promise<void> {
  await request.post('/api/providers/set-default', { id })
}

// ─── Channels ────────────────────────────────────────────────────────────────

export type ChannelType = 'web' | 'feishu' | 'wecom' | 'wechat'

export type FeishuChannelSettings = {
  appId: string
  appSecret: string
  encryptKey: string
  verificationToken: string
  connectionMode: 'websocket' | 'webhook'
  apiBaseUrl?: string
  allowedDocTokens?: string
  allowedBitableTokens?: string
  allowedWikiSpaceIds?: string
}

export type ChannelConfig = {
  id: string
  displayName: string
  channelType: ChannelType
  providerId: string
  isEnabled: boolean
  settings: string // JSON string
}

export type ChannelCreateRequest = {
  displayName: string
  channelType: ChannelType
  providerId: string
  isEnabled: boolean
  settings?: string
}

export type ChannelUpdateRequest = {
  id: string
  displayName?: string
  channelType?: ChannelType
  providerId?: string
  isEnabled: boolean
  settings?: string
}

export type ChannelTestResult = {
  success: boolean
  message: string
  latencyMs: number
  connectivityHint?: string
}

export type ChannelPublishRequest = {
  targetId: string
  content: string
}

export type ChannelStats = {
  channelId: string
  signatureFailures: number
  aiCallFailures: number
  replyFailures: number
}

export type ChannelHealth = {
  channelId: string
  connectionMode: string
  connectionStatus: string
  tokenRemainingSeconds: number | null
  lastMessageAt: string | null
  lastMessageSuccess: boolean | null
  lastMessageError: string | null
}

export type ChannelTypeInfo = {
  type: string
  displayName: string
  canCreate: boolean
}

export type ChannelToolInfo = {
  name: string
  description: string
}

export async function listChannels(): Promise<ChannelConfig[]> {
  const { data } = await request.get<ChannelConfig[]>('/api/channels')
  return data
}

export async function createChannel(req: ChannelCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/channels', req)
  return data
}

export async function updateChannel(req: ChannelUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/channels/update', req)
  return data
}

export async function deleteChannel(id: string): Promise<void> {
  await request.post('/api/channels/delete', { id })
}

export async function testChannel(id: string): Promise<ChannelTestResult> {
  const { data } = await request.post<ChannelTestResult>(`/api/channels/${id}/test`)
  return data
}

export async function publishChannelMessage(id: string, req: ChannelPublishRequest): Promise<void> {
  await request.post(`/api/channels/${id}/publish`, req)
}

export async function getChannelStats(id: string): Promise<ChannelStats> {
  const { data } = await request.get<ChannelStats>(`/api/channels/${id}/stats`)
  return data
}

export async function getChannelHealth(id: string): Promise<ChannelHealth> {
  const { data } = await request.get<ChannelHealth>(`/api/channels/${id}/health`)
  return data
}

export async function getChannelTypes(): Promise<ChannelTypeInfo[]> {
  const { data } = await request.get<ChannelTypeInfo[]>('/api/channels/types')
  return data
}

export async function getChannelTools(channelType: string): Promise<ChannelToolInfo[]> {
  const { data } = await request.get<ChannelToolInfo[]>(`/api/channels/${channelType}/tools`)
  return data
}

// ─── Sessions ────────────────────────────────────────────────────────────────

export type MessageAttachment = {
  fileName: string
  mimeType: string
  base64Data: string
}

export type MessageSource = 'cron' | 'skill' | 'tool'
export const SYSTEM_SOURCES = new Set<MessageSource>(['cron', 'skill', 'tool'])

export type SessionMessage = {
  role: 'user' | 'assistant'
  content: string
  thinkContent?: string | null
  timestamp: string
  attachments?: MessageAttachment[] | null
  source?: MessageSource | null
}

export type SessionInfo = {
  id: string
  title: string
  providerId: string
  isApproved: boolean
  channelType: ChannelType
  channelId: string
  createdAt: string
  agentId?: string | null
  approvalReason?: string | null
}

export type CreateSessionRequest = {
  title: string
  providerId: string
  channelId?: string
  agentId?: string
}

export type ChatRequest = {
  content: string
  attachments?: MessageAttachment[]
}

export type SseChunk =
  | { type: 'token'; content: string }
  | { type: 'done'; thinkContent?: string | null }
  | { type: 'error'; message: string }

export interface PagedMessagesResponse {
  messages: SessionMessage[]
  total: number
  hasMore: boolean
}

export async function listSessions(): Promise<SessionInfo[]> {
  const { data } = await request.get<SessionInfo[]>('/api/sessions')
  return data
}

export async function createSession(req: CreateSessionRequest): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions', req)
  return data
}

export async function deleteSession(id: string): Promise<void> {
  await request.post('/api/sessions/delete', { id })
}

export async function approveSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions/approve', { id, reason })
  return data
}

export async function disableSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions/disable', { id, reason })
  return data
}

export async function getMessagesPaged(
  sessionId: string,
  skip: number,
  limit: number,
): Promise<PagedMessagesResponse> {
  const { data } = await request.get<PagedMessagesResponse>(
    `/api/sessions/${sessionId}/messages`,
    { params: { skip, limit } },
  )
  return data
}

export async function switchSessionProvider(id: string, providerId: string): Promise<void> {
  await request.post('/api/sessions/switch-provider', { id, providerId })
}

/**
 * SSE 流式对话。通过 Fetch API 发送请求并逐 chunk 回调。
 * 返回 AbortController 供调用方取消。
 */
export function streamChat(
  sessionId: string,
  req: ChatRequest,
  onChunk: (chunk: SseChunk) => void,
  onError: (err: string) => void,
  onDone: () => void,
): AbortController {
  const controller = new AbortController()
  const token = useAuthStore.getState().token

  ;(async () => {
    try {
      const response = await fetch(`/api/sessions/${sessionId}/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(req),
        signal: controller.signal,
      })

      if (!response.ok) {
        const body = await response.json().catch(() => ({ message: response.statusText }))
        onError((body as { message?: string }).message ?? response.statusText)
        return
      }

      const reader = response.body!.getReader()
      const decoder = new TextDecoder()
      let buffer = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          const trimmed = line.trim()
          if (!trimmed.startsWith('data:')) continue
          const raw = trimmed.slice(5).trim()
          if (raw === '[DONE]') {
            onDone()
            return
          }
          try {
            const chunk = JSON.parse(raw) as SseChunk
            if (chunk.type === 'error') {
              onError(chunk.message)
              return
            }
            onChunk(chunk)
          } catch {
            // ignore invalid JSON lines
          }
        }
      }
      onDone()
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') return
      onError(String(err))
    }
  })()

  return controller
}

// ─── Session DNA ──────────────────────────────────────────────────────────────

export type SessionDnaFileInfo = {
  fileName: string
  description: string
  content: string
  updatedAt: string
}

export type FeishuDocImportResult = {
  success: boolean
  file: SessionDnaFileInfo
  charCount: number
}

export async function listSessionDna(sessionId: string): Promise<SessionDnaFileInfo[]> {
  const { data } = await request.get<SessionDnaFileInfo[]>(`/api/sessions/${sessionId}/dna`)
  return data
}

export async function getSessionDnaFile(sessionId: string, fileName: string): Promise<SessionDnaFileInfo> {
  const { data } = await request.get<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna/${fileName}`)
  return data
}

export async function updateSessionDna(
  sessionId: string,
  fileName: string,
  content: string,
): Promise<SessionDnaFileInfo> {
  const { data } = await request.post<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna`, {
    fileName,
    content,
  })
  return data
}

export async function importSessionDnaFromFeishu(
  sessionId: string,
  docUrlOrToken: string,
  fileName: string,
): Promise<FeishuDocImportResult> {
  const { data } = await request.post<FeishuDocImportResult>(
    `/api/sessions/${sessionId}/dna/import-from-feishu`,
    { docUrlOrToken, fileName },
  )
  return data
}

// ─── Session Memory ───────────────────────────────────────────────────────────

export async function getSessionMemory(sessionId: string): Promise<string> {
  const { data } = await request.get<{ content: string }>(`/api/sessions/${sessionId}/memory`)
  return data.content
}

export async function updateSessionMemory(sessionId: string, content: string): Promise<string> {
  const { data } = await request.post<{ content: string }>(`/api/sessions/${sessionId}/memory`, {
    content,
  })
  return data.content
}

// ─── Agents ──────────────────────────────────────────────────────────────────

export type AgentConfig = {
  id: string
  name: string
  description: string
  isEnabled: boolean
  isDefault: boolean
  boundSkillIds: string[]
  enabledMcpServerIds: string[]
  createdAtUtc: string
}

export type AgentCreateRequest = {
  name: string
  description?: string
  isEnabled?: boolean
  boundSkillIds?: string[]
  enabledMcpServerIds?: string[]
}

export type AgentUpdateRequest = {
  id: string
  name?: string
  description?: string
  isEnabled?: boolean
  boundSkillIds?: string[]
  enabledMcpServerIds?: string[]
}

export type ToolItem = {
  name: string
  description: string
  isEnabled: boolean
}

export type ToolGroup = {
  id: string
  name: string
  type: 'builtin' | 'mcp'
  isEnabled: boolean
  tools: ToolItem[]
}

export type AgentToolsResponse = {
  groups: ToolGroup[]
}

export type ToolGroupConfig = {
  groupId: string
  isEnabled: boolean
  disabledToolNames?: string[]
}

export async function listAgents(): Promise<AgentConfig[]> {
  const { data } = await request.get<AgentConfig[]>('/api/agents')
  return data
}

export async function getAgent(id: string): Promise<AgentConfig> {
  const { data } = await request.get<AgentConfig>(`/api/agents/${id}`)
  return data
}

export async function createAgent(req: AgentCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/agents', req)
  return data
}

export async function updateAgent(req: AgentUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/agents/update', req)
  return data
}

export async function deleteAgent(id: string): Promise<void> {
  await request.post('/api/agents/delete', { id })
}

export async function listAgentTools(agentId: string): Promise<AgentToolsResponse> {
  const { data } = await request.get<AgentToolsResponse>(`/api/agents/${agentId}/tools`)
  return data
}

export async function updateAgentToolSettings(
  agentId: string,
  configs: ToolGroupConfig[],
): Promise<void> {
  await request.post(`/api/agents/${agentId}/tools/settings`, configs)
}

// ─── Agent DNA ────────────────────────────────────────────────────────────────

export type AgentDnaFileInfo = {
  fileName: string
  description: string
  content: string
  updatedAt: string
}

export async function listAgentDna(agentId: string): Promise<AgentDnaFileInfo[]> {
  const { data } = await request.get<AgentDnaFileInfo[]>(`/api/agents/${agentId}/dna`)
  return data
}

export async function getAgentDnaFile(agentId: string, fileName: string): Promise<AgentDnaFileInfo> {
  const { data } = await request.get<AgentDnaFileInfo>(`/api/agents/${agentId}/dna/${fileName}`)
  return data
}

export async function updateAgentDna(
  agentId: string,
  fileName: string,
  content: string,
): Promise<{ success: boolean }> {
  const { data } = await request.post<{ success: boolean }>(`/api/agents/${agentId}/dna`, {
    fileName,
    content,
  })
  return data
}

// ─── Skills ───────────────────────────────────────────────────────────────────

export type SkillType = 'python' | 'nodejs' | 'shell' | 'csharp'

export type SkillConfig = {
  id: string
  name: string
  description: string
  skillType: SkillType
  entryPoint: string
  isEnabled: boolean
  createdAtUtc: string
}

export type SkillCreateRequest = {
  name: string
  description?: string
  skillType: SkillType
  entryPoint: string
  isEnabled?: boolean
}

export type SkillUpdateRequest = {
  id: string
  name?: string
  description?: string
  skillType?: SkillType
  entryPoint?: string
  isEnabled?: boolean
}

export type SkillFileInfo = {
  path: string
  sizeBytes: number
}

export async function listSkills(): Promise<SkillConfig[]> {
  const { data } = await request.get<SkillConfig[]>('/api/skills')
  return data
}

export async function getSkill(id: string): Promise<SkillConfig> {
  const { data } = await request.get<SkillConfig>(`/api/skills/${id}`)
  return data
}

export async function createSkill(req: SkillCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/skills', req)
  return data
}

export async function updateSkill(req: SkillUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/skills/update', req)
  return data
}

export async function deleteSkill(id: string): Promise<void> {
  await request.post('/api/skills/delete', { id })
}

export async function listSkillFiles(skillId: string): Promise<SkillFileInfo[]> {
  const { data } = await request.get<SkillFileInfo[]>(`/api/skills/${skillId}/files`)
  return data
}

export async function getSkillFileContent(skillId: string, filePath: string): Promise<string> {
  const { data } = await request.get<{ content: string }>(`/api/skills/${skillId}/files/${filePath}`)
  return data.content
}

export async function writeSkillFile(
  skillId: string,
  fileName: string,
  content: string,
): Promise<void> {
  await request.post(`/api/skills/${skillId}/files`, { fileName, content })
}

export async function deleteSkillFile(skillId: string, fileName: string): Promise<void> {
  await request.post(`/api/skills/${skillId}/files/delete`, { fileName })
}

// ─── MCP Servers ─────────────────────────────────────────────────────────────

export type McpTransportType = 'stdio' | 'sse' | 'http'

export type McpServerConfig = {
  id: string
  name: string
  transportType: McpTransportType
  command?: string | null
  args?: string[] | null
  env?: Record<string, string | null> | null
  url?: string | null
  headers?: Record<string, string> | null
  isEnabled: boolean
  createdAtUtc: string
}

export type McpServerCreateRequest = {
  name: string
  transportType?: McpTransportType
  command?: string
  args?: string[]
  env?: Record<string, string>
  url?: string
  headers?: Record<string, string>
  isEnabled?: boolean
}

export type McpServerUpdateRequest = {
  id: string
  name?: string
  transportType?: McpTransportType
  command?: string
  args?: string[]
  env?: Record<string, string>
  url?: string
  headers?: Record<string, string>
  isEnabled?: boolean
}

export type McpToolInfo = {
  name: string
  description?: string
}

export type McpTestResult = {
  success: boolean
  toolCount?: number
  toolNames?: string[]
  error?: string
}

export async function listMcpServers(): Promise<McpServerConfig[]> {
  const { data } = await request.get<McpServerConfig[]>('/api/mcp-servers')
  return data
}

export async function createMcpServer(req: McpServerCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/mcp-servers', req)
  return data
}

export async function updateMcpServer(req: McpServerUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/mcp-servers/update', req)
  return data
}

export async function deleteMcpServer(id: string): Promise<void> {
  await request.post('/api/mcp-servers/delete', { id })
}

export async function testMcpServer(id: string): Promise<McpTestResult> {
  const { data } = await request.post<McpTestResult>(`/api/mcp-servers/${id}/test`)
  return data
}

export async function listMcpServerTools(id: string): Promise<McpToolInfo[]> {
  const { data } = await request.get<McpToolInfo[]>(`/api/mcp-servers/${id}/tools`)
  return data
}

// ─── Global Tools ─────────────────────────────────────────────────────────────

export type GlobalToolGroup = ToolGroup & {
  loadError?: boolean
}

export async function listAllTools(): Promise<GlobalToolGroup[]> {
  const { data } = await request.get<GlobalToolGroup[]>('/api/tools')
  return data
}

// ─── Usage Statistics ─────────────────────────────────────────────────────────

export type DailyUsage = {
  date: string
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
}

export type ProviderUsage = {
  providerId: string
  providerName: string
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
}

export type SourceUsage = {
  source: string
  inputTokens: number
  outputTokens: number
}

export type UsageSummary = {
  totalInputTokens: number
  totalOutputTokens: number
  totalCostUsd: number
}

export type UsageQueryResult = {
  daily: DailyUsage[]
  byProvider: ProviderUsage[]
  bySource: SourceUsage[]
  summary: UsageSummary
}

export async function fetchUsageStats(startDate: string, endDate: string): Promise<UsageQueryResult> {
  const { data } = await request.post<UsageQueryResult>('/api/usage/query', { startDate, endDate })
  return data
}
