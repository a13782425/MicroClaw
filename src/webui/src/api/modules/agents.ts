import request from '../request'

export type AgentConfig = {
  id: string
  name: string
  description: string
  isEnabled: boolean
  isDefault: boolean
  disabledSkillIds: string[]
  disabledMcpServerIds: string[]
  createdAtUtc: string
  exposeAsA2A: boolean
  allowedSubAgentIds: string[] | null
  routingStrategy: string
  monthlyBudgetUsd: number | null
  contextWindowMessages: number | null
}

export type AgentCreateRequest = {
  name: string
  description?: string
  isEnabled?: boolean
  disabledSkillIds?: string[]
  disabledMcpServerIds?: string[]
  allowedSubAgentIds?: string[] | null
}

export type AgentUpdateRequest = {
  id: string
  name?: string
  description?: string
  isEnabled?: boolean
  disabledSkillIds?: string[]
  disabledMcpServerIds?: string[]
  exposeAsA2A?: boolean
  allowedSubAgentIds?: string[] | null
  hasAllowedSubAgentIds?: boolean
  routingStrategy?: string
  monthlyBudgetUsd?: number | null
  hasMonthlyBudgetUsd?: boolean
  contextWindowMessages?: number | null
  hasContextWindowMessages?: boolean
}

export type ToolItem = {
  name: string
  description: string
  isEnabled: boolean
}

export type ToolGroup = {
  id: string
  name: string
  type: 'builtin' | 'channel' | 'mcp'
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

export type SubAgentInfo = {
  id: string
  name: string
  description: string
}

export type AgentDnaFileInfo = {
  fileName: string
  description: string
  content: string
  updatedAt: string
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

export async function listSubAgents(agentId: string): Promise<SubAgentInfo[]> {
  const { data } = await request.get<SubAgentInfo[]>(`/api/agents/${agentId}/sub-agents`)
  return data
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