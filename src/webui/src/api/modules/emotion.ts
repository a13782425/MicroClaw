import request from '../request'

export type EmotionStateDto = {
  alertness: number
  mood: number
  curiosity: number
  confidence: number
}

export type EmotionSnapshotDto = {
  alertness: number
  mood: number
  curiosity: number
  confidence: number
  recordedAtMs: number
}

export type EmotionHistoryRequest = {
  from: number
  to: number
}

export async function getAgentEmotionCurrent(agentId: string): Promise<EmotionStateDto> {
  const { data } = await request.get<EmotionStateDto>(`/api/agents/${agentId}/emotion/current`)
  return data
}

export async function getAgentEmotionHistory(
  agentId: string,
  req: EmotionHistoryRequest,
): Promise<EmotionSnapshotDto[]> {
  const { data } = await request.post<EmotionSnapshotDto[]>(
    `/api/agents/${agentId}/emotion/history`,
    req,
  )
  return data
}