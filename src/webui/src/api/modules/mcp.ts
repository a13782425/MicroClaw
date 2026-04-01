import request from '../request'

export type McpTransportType = 'stdio' | 'sse' | 'http'
export type McpServerSource = 'manual' | 'plugin'

export type McpEnvVarInfo = {
  name: string
  isSet: boolean
  foundIn: string
}

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
  source: McpServerSource
  pluginId?: string | null
  pluginName?: string | null
  requiredEnvVars?: McpEnvVarInfo[] | null
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