import axios from 'axios'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { router } from '@/router'

export type GatewayHealth = {
  status: string
  service: string
  utcNow: string
  version: string
}

export type ProviderProtocol = 'openai' | 'anthropic'

export type ProviderCapabilities = {
  // 输入模态
  inputText: boolean
  inputImage: boolean
  inputAudio: boolean
  inputVideo: boolean
  inputFile: boolean
  // 输出模态
  outputText: boolean
  outputImage: boolean
  outputAudio: boolean
  outputVideo: boolean
  // 特殊能力
  supportsFunctionCalling: boolean
  supportsResponsesApi: boolean
  // 价格（$/1M tokens）
  inputPricePerMToken: number | null
  outputPricePerMToken: number | null
  cacheInputPricePerMToken: number | null
  cacheOutputPricePerMToken: number | null
  // 备注
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

export type ProviderDeleteRequest = {
  id: string
}

axios.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.token) {
    config.headers.Authorization = 'Bearer ' + auth.token
  }
  return config
})

axios.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore().clearAuth()
      router.push({ name: 'login' })
    } else if (error.response) {
      // 提取后端标准错误格式 { success: false, message: "...", errorCode: "..." }
      const message = error.response.data?.message
      ElMessage.error(message || '操作失败，请稍后重试')
    } else {
      ElMessage.error('网络请求失败，请检查连接')
    }
    return Promise.reject(error)
  }
)

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await axios.get<GatewayHealth>('/api/health')
  return data
}

export async function login(username: string, password: string) {
  const { data } = await axios.post('/api/auth/login', { username, password })
  return data as {
    token: string
    username: string
    role: string
    expiresAtUtc: string
  }
}

export async function listProviders(): Promise<ProviderConfig[]> {
  const { data } = await axios.get<ProviderConfig[]>('/api/providers')
  return data
}

export async function createProvider(req: ProviderCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/providers', req)
  return data
}

export async function updateProvider(req: ProviderUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/providers/update', req)
  return data
}

export async function deleteProvider(id: string): Promise<void> {
  await axios.post('/api/providers/delete', { id })
}

export async function setDefaultProvider(id: string): Promise<void> {
  await axios.post('/api/providers/set-default', { id })
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
  /** F-G-3: 允许 Agent 访问的文档 Token 白名单（逗号分隔字符串，空表示不限制） */
  allowedDocTokens?: string
  /** F-G-3: 允许 Agent 访问的多维表格 App Token 白名单（逗号分隔字符串，空表示不限制） */
  allowedBitableTokens?: string
  /** F-G-3: 允许 Agent 访问的知识库 Space ID 白名单（逗号分隔字符串，空表示不限制） */
  allowedWikiSpaceIds?: string
}

export type ChannelConfig = {
  id: string
  displayName: string
  channelType: ChannelType
  providerId: string
  isEnabled: boolean
  settings: string // JSON string, parsed per channel type
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

export async function listChannels(): Promise<ChannelConfig[]> {
  const { data } = await axios.get<ChannelConfig[]>('/api/channels')
  return data
}

export async function createChannel(req: ChannelCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/channels', req)
  return data
}

export async function updateChannel(req: ChannelUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/channels/update', req)
  return data
}

export async function deleteChannel(id: string): Promise<void> {
  await axios.post('/api/channels/delete', { id })
}

export type ChannelTestResult = {
  success: boolean
  message: string
  latencyMs: number
  /** F-E-3: Webhook 模式下探测到内网环境时的建议提示，null 表示无提示 */
  connectivityHint?: string
}

export async function testChannel(id: string): Promise<ChannelTestResult> {
  const { data } = await axios.post<ChannelTestResult>(`/api/channels/${id}/test`)
  return data
}

export type ChannelPublishRequest = {
  targetId: string
  content: string
}

export async function publishChannelMessage(id: string, req: ChannelPublishRequest): Promise<void> {
  await axios.post(`/api/channels/${id}/publish`, req)
}

export type ChannelStats = {
  channelId: string
  signatureFailures: number
  aiCallFailures: number
  replyFailures: number
}

export async function getChannelStats(id: string): Promise<ChannelStats> {
  const { data } = await axios.get<ChannelStats>(`/api/channels/${id}/stats`)
  return data
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

export async function getChannelHealth(id: string): Promise<ChannelHealth> {
  const { data } = await axios.get<ChannelHealth>(`/api/channels/${id}/health`)
  return data
}

// ─── Sessions ────────────────────────────────────────────────────────────────

export type MessageAttachment = {
  fileName: string
  mimeType: string
  base64Data: string
}

/** 系统自动触发类消息的来源标记，此类 user 消息不在 Web 界面展示 */
export type MessageSource = 'cron' | 'skill' | 'tool'

/** source 属于系统触发类型的集合，过滤时使用 */
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

export async function listSessions(): Promise<SessionInfo[]> {
  const { data } = await axios.get<SessionInfo[]>('/api/sessions')
  return data
}

export async function createSession(req: CreateSessionRequest): Promise<SessionInfo> {
  const { data } = await axios.post<SessionInfo>('/api/sessions', req)
  return data
}

export async function deleteSession(id: string): Promise<void> {
  await axios.post('/api/sessions/delete', { id })
}

export async function approveSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await axios.post<SessionInfo>('/api/sessions/approve', { id, reason })
  return data
}

export async function disableSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await axios.post<SessionInfo>('/api/sessions/disable', { id, reason })
  return data
}

export async function getMessages(sessionId: string): Promise<SessionMessage[]> {
  const { data } = await axios.get<SessionMessage[]>(`/api/sessions/${sessionId}/messages`)
  return data
}

export interface PagedMessagesResponse {
  messages: SessionMessage[]
  total: number
  hasMore: boolean
}

export async function getMessagesPaged(
  sessionId: string,
  skip: number,
  limit: number
): Promise<PagedMessagesResponse> {
  const { data } = await axios.get<PagedMessagesResponse>(
    `/api/sessions/${sessionId}/messages`,
    { params: { skip, limit } }
  )
  return data
}

/**
 * SSE 流式对话，通过 fetch 发送请求并逐 chunk 回调。
 * 返回 AbortController 以供调用方取消。
 */
export function streamChat(
  sessionId: string,
  req: ChatRequest,
  onChunk: (chunk: SseChunk) => void,
  onError: (err: string) => void,
  onDone: () => void
): AbortController {
  const controller = new AbortController()
  const auth = useAuthStore()

  ;(async () => {
    try {
      const response = await fetch(`/api/sessions/${sessionId}/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: 'Bearer ' + auth.token
        },
        body: JSON.stringify(req),
        signal: controller.signal
      })

      if (!response.ok) {
        const body = await response.json().catch(() => ({ message: response.statusText }))
        onError(body.message ?? response.statusText)
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
              onError((chunk as { type: 'error'; message: string }).message)
              return
            }
            onChunk(chunk)
          } catch {
            // 忽略非法 JSON 行
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

// B-03：Session DNA 固定文件（SOUL / USER / AGENTS）
export type SessionDnaFileInfo = {
  fileName: string
  description: string
  content: string
  updatedAt: string
}

export type McpTool = {
  name: string
  description: string
}


export async function listAgents(): Promise<AgentConfig[]> {
  const { data } = await axios.get<AgentConfig[]>('/api/agents')
  return data
}

export async function getAgent(id: string): Promise<AgentConfig> {
  const { data } = await axios.get<AgentConfig>(`/api/agents/${id}`)
  return data
}

export async function createAgent(req: AgentCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/agents', req)
  return data
}

export async function updateAgent(req: AgentUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/agents/update', req)
  return data
}

export async function deleteAgent(id: string): Promise<void> {
  await axios.post('/api/agents/delete', { id })
}

export async function listAgentTools(agentId: string): Promise<AgentToolsResponse> {
  const { data } = await axios.get<AgentToolsResponse>(`/api/agents/${agentId}/tools`)
  return data
}

export async function updateAgentToolSettings(
  agentId: string,
  configs: ToolGroupConfig[]
): Promise<void> {
  await axios.post(`/api/agents/${agentId}/tools/settings`, configs)
}

export async function switchSessionProvider(
  id: string,
  providerId: string
): Promise<void> {
  await axios.post('/api/sessions/switch-provider', { id, providerId })
}

// ─── Skills ───────────────────────────────────────────────────────────────────

export type SkillType = 'python' | 'nodejs' | 'shell'

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
  const { data } = await axios.get<SkillConfig[]>('/api/skills')
  return data
}

export async function getSkill(id: string): Promise<SkillConfig> {
  const { data } = await axios.get<SkillConfig>(`/api/skills/${id}`)
  return data
}

export async function createSkill(req: SkillCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/skills', req)
  return data
}

export async function updateSkill(req: SkillUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/skills/update', req)
  return data
}

export async function deleteSkill(id: string): Promise<void> {
  await axios.post('/api/skills/delete', { id })
}

export async function listSkillFiles(skillId: string): Promise<SkillFileInfo[]> {
  const { data } = await axios.get<SkillFileInfo[]>(`/api/skills/${skillId}/files`)
  return data
}

export async function getSkillFileContent(skillId: string, filePath: string): Promise<string> {
  const { data } = await axios.get<{ content: string }>(`/api/skills/${skillId}/files/${filePath}`)
  return data.content
}

export async function writeSkillFile(
  skillId: string,
  fileName: string,
  content: string
): Promise<void> {
  await axios.post(`/api/skills/${skillId}/files`, { fileName, content })
}

export async function deleteSkillFile(skillId: string, fileName: string): Promise<void> {
  await axios.post(`/api/skills/${skillId}/files/delete`, { fileName })
}

export async function getAgentSkills(agentId: string): Promise<string[]> {
  const agent = await getAgent(agentId)
  return agent.boundSkillIds ?? []
}

export async function updateAgentSkills(agentId: string, skillIds: string[]): Promise<void> {
  await axios.post('/api/agents/update', { id: agentId, boundSkillIds: skillIds })
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
  const { data } = await axios.post<UsageQueryResult>('/api/usage/query', { startDate, endDate })
  return data
}

// ─── Session DNA 飞书文档导入（F-C-6）──────────────────────────────────────────

export type FeishuDocImportResult = {
  success: boolean
  file: SessionDnaFileInfo
  charCount: number
}

export async function importSessionDnaFromFeishu(
  sessionId: string,
  docUrlOrToken: string,
  fileName: string
): Promise<FeishuDocImportResult> {
  const { data } = await axios.post<FeishuDocImportResult>(
    `/api/sessions/${sessionId}/dna/import-from-feishu`,
    { docUrlOrToken, fileName }
  )
  return data
}



export async function listSessionDna(sessionId: string): Promise<SessionDnaFileInfo[]> {
  const { data } = await axios.get<SessionDnaFileInfo[]>(`/api/sessions/${sessionId}/dna`)
  return data
}

export async function getSessionDnaFile(sessionId: string, fileName: string): Promise<SessionDnaFileInfo> {
  const { data } = await axios.get<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna/${fileName}`)
  return data
}

export async function updateSessionDna(
  sessionId: string,
  fileName: string,
  content: string
): Promise<SessionDnaFileInfo> {
  const { data } = await axios.post<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna`, {
    fileName,
    content,
  })
  return data
}

// ─── Session Memory ───────────────────────────────────────────────────────────

export type DailyMemoryInfo = {
  date: string
  content: string
  updatedAt: string
}

export async function getSessionMemory(sessionId: string): Promise<string> {
  const { data } = await axios.get<{ content: string }>(`/api/sessions/${sessionId}/memory`)
  return data.content
}

export async function updateSessionMemory(sessionId: string, content: string): Promise<string> {
  const { data } = await axios.post<{ content: string }>(`/api/sessions/${sessionId}/memory`, { content })
  return data.content
}

export async function listSessionDailyMemories(sessionId: string): Promise<string[]> {
  const { data } = await axios.get<{ dates: string[] }>(`/api/sessions/${sessionId}/memory/daily`)
  return data.dates
}

export async function getSessionDailyMemory(sessionId: string, date: string): Promise<DailyMemoryInfo> {
  const { data } = await axios.get<DailyMemoryInfo>(`/api/sessions/${sessionId}/memory/daily/${date}`)
  return data
}

// ─── MCP Servers ─────────────────────────────────────────────────────────────

export type McpTransportType = 'stdio' | 'sse'

export type McpServerConfig = {
  id: string
  name: string
  transportType: McpTransportType
  command?: string | null
  args?: string[] | null
  env?: Record<string, string | null> | null
  url?: string | null
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
  const { data } = await axios.get<McpServerConfig[]>('/api/mcp-servers')
  return data
}

export async function createMcpServer(req: McpServerCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/mcp-servers', req)
  return data
}

export async function updateMcpServer(req: McpServerUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/mcp-servers/update', req)
  return data
}

export async function deleteMcpServer(id: string): Promise<void> {
  await axios.post('/api/mcp-servers/delete', { id })
}

export async function testMcpServer(id: string): Promise<McpTestResult> {
  const { data } = await axios.post<McpTestResult>(`/api/mcp-servers/${id}/test`)
  return data
}

export async function listMcpServerTools(id: string): Promise<McpToolInfo[]> {
  const { data } = await axios.get<McpToolInfo[]>(`/api/mcp-servers/${id}/tools`)
  return data
}
