import request from '../request'

// ── 类型定义 ──────────────────────────────────────────────────────────────

export type PetEmotionDto = {
  alertness: number
  mood: number
  curiosity: number
  confidence: number
}

export type PetRateLimitDto = {
  maxCalls: number
  usedCalls: number
  remainingCalls: number
  isExhausted: boolean
}

export type PetStatusDto = {
  sessionId: string
  behaviorState: string
  emotion: PetEmotionDto
  enabled: boolean
  rateLimit: PetRateLimitDto | null
  lastHeartbeatAt: string | null
  createdAt: string
  updatedAt: string
}

export type PetConfigDto = {
  enabled: boolean
  activeHoursStart: number | null
  activeHoursEnd: number | null
  maxLlmCallsPerWindow: number
  windowHours: number
  allowedAgentIds: string[]
  preferredProviderId: string | null
  socialMode: boolean
}

export type UpdatePetConfigRequest = {
  enabled?: boolean
  activeHoursStart?: number | null
  activeHoursEnd?: number | null
  maxLlmCallsPerWindow?: number
  windowHours?: number
  allowedAgentIds?: string[]
  preferredProviderId?: string | null
  socialMode?: boolean
}

export type PetJournalResponse = {
  entries: string[]
  count: number
}

export type PetKnowledgeDto = {
  chunkCount: number
  dbSizeBytes: number
}

export type PersonalityDto = {
  persona: string
  tone: string
  language: string
}

export type DispatchRuleDto = {
  pattern: string
  preferredModelType: string
  notes: string
}

export type DispatchRulesDto = {
  defaultStrategy: string
  rules: DispatchRuleDto[]
}

export type KnowledgeTopicDto = {
  name: string
  description: string
  priority: string
}

export type KnowledgeInterestsDto = {
  topics: KnowledgeTopicDto[]
}

export type PetPromptsDto = {
  personality: PersonalityDto
  dispatchRules: DispatchRulesDto
  knowledgeInterests: KnowledgeInterestsDto
}

export type UpdatePersonalityRequest = {
  persona?: string
  tone?: string
  language?: string
}

export type UpdateDispatchRuleRequest = {
  pattern: string
  preferredModelType: string
  notes?: string
}

export type UpdateDispatchRulesRequest = {
  defaultStrategy?: string
  rules?: UpdateDispatchRuleRequest[]
}

export type UpdateKnowledgeTopicRequest = {
  name: string
  description: string
  priority?: string
}

export type UpdateKnowledgeInterestsRequest = {
  topics?: UpdateKnowledgeTopicRequest[]
}

export type UpdatePetPromptsRequest = {
  personality?: UpdatePersonalityRequest
  dispatchRules?: UpdateDispatchRulesRequest
  knowledgeInterests?: UpdateKnowledgeInterestsRequest
}

// ── SignalR 事件类型 ──────────────────────────────────────────────────────

export type PetEmotionSnapshotDto = {
  state: PetEmotionDto
  recordedAtMs: number
}

export type PetMessageEvent = {
  sessionId: string
  message: string
}

export type PetStateChangedEvent = {
  sessionId: string
  newState: string
  reason: string
}

export type PetActionEvent = {
  sessionId: string
  actionType: string
  detail?: string
}

// ── API 调用函数 ──────────────────────────────────────────────────────────

export async function getPetStatus(sessionId: string): Promise<PetStatusDto> {
  const { data } = await request.get<PetStatusDto>(`/api/sessions/${sessionId}/pet`)
  return data
}

export async function updatePetConfig(
  sessionId: string,
  req: UpdatePetConfigRequest,
): Promise<PetConfigDto> {
  const { data } = await request.post<PetConfigDto>(
    `/api/sessions/${sessionId}/pet/config`,
    req,
  )
  return data
}

export async function getPetJournal(
  sessionId: string,
  limit?: number,
): Promise<PetJournalResponse> {
  const { data } = await request.get<PetJournalResponse>(
    `/api/sessions/${sessionId}/pet/journal`,
    { params: limit ? { limit } : undefined },
  )
  return data
}

export async function getPetKnowledge(sessionId: string): Promise<PetKnowledgeDto> {
  const { data } = await request.get<PetKnowledgeDto>(
    `/api/sessions/${sessionId}/pet/knowledge`,
  )
  return data
}

export async function getPetPrompts(sessionId: string): Promise<PetPromptsDto> {
  const { data } = await request.get<PetPromptsDto>(
    `/api/sessions/${sessionId}/pet/prompts`,
  )
  return data
}

export async function updatePetPrompts(
  sessionId: string,
  req: UpdatePetPromptsRequest,
): Promise<{ success: boolean }> {
  const { data } = await request.post<{ success: boolean }>(
    `/api/sessions/${sessionId}/pet/prompts`,
    req,
  )
  return data
}

export async function getPetEmotionHistory(
  sessionId: string,
  from?: number,
  to?: number,
): Promise<PetEmotionSnapshotDto[]> {
  const { data } = await request.get<PetEmotionSnapshotDto[]>(
    `/api/sessions/${sessionId}/pet/emotion/history`,
    { params: { from, to } },
  )
  return data
}
