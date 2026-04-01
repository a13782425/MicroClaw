import request from '../request'

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

export type DailyProviderUsage = {
  date: string
  providerId: string
  providerName: string
  estimatedCostUsd: number
}

export type UsageSummary = {
  totalInputTokens: number
  totalOutputTokens: number
  totalCostUsd: number
}

export type AgentUsage = {
  agentId: string
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
}

export type SessionUsage = {
  sessionId: string
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
}

export type UsageQueryResult = {
  daily: DailyUsage[]
  byProvider: ProviderUsage[]
  bySource: SourceUsage[]
  dailyByProvider: DailyProviderUsage[]
  summary: UsageSummary
  byAgent: AgentUsage[]
  bySession: SessionUsage[]
}

export async function fetchUsageStats(
  startDate: string,
  endDate: string,
  agentId?: string,
  sessionId?: string,
): Promise<UsageQueryResult> {
  const { data } = await request.post<UsageQueryResult>('/api/usage/query', {
    startDate,
    endDate,
    agentId: agentId || undefined,
    sessionId: sessionId || undefined,
  })
  return data
}