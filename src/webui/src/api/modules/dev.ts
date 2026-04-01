import request from '../request'

export type ToolStatsDto = {
  callCount: number
  errorCount: number
  totalElapsedMs: number
  maxElapsedMs: number
  averageElapsedMs: number
}

export type AgentRunRecord = {
  agentId: string
  success: boolean
  durationMs: number
  executedAt: string
}

export type DevMetricsSnapshot = {
  startedAt: string
  totalAgentRuns: number
  failedAgentRuns: number
  toolStats: Record<string, ToolStatsDto>
  recentRuns: AgentRunRecord[]
}

export type ContextProviderInfoDto = {
  name: string
  order: number
}

export type MiddlewareLimitsDto = {
  iterations: { min: number; max: number }
  maxDepth: { default: number }
}

export async function getDevMetrics(): Promise<DevMetricsSnapshot> {
  const { data } = await request.get<DevMetricsSnapshot>('/dev/metrics')
  return data
}

export async function getDevContextProviders(): Promise<ContextProviderInfoDto[]> {
  const { data } = await request.get<ContextProviderInfoDto[]>('/dev/context-providers')
  return data
}

export async function getDevMiddlewareLimits(): Promise<MiddlewareLimitsDto> {
  const { data } = await request.get<MiddlewareLimitsDto>('/dev/middleware-limits')
  return data
}