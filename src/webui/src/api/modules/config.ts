import request from '../request'

export type AgentConfigSection = {
  subAgentMaxDepth: number
}

export type SkillsConfigSection = {
  additionalFolders: string[]
}

export type BehaviorProfileConfigSection = {
  temperature?: number
  topP?: number
  systemPromptSuffix?: string
}

export type EmotionDeltaConfigSection = {
  alertness?: number
  mood?: number
  curiosity?: number
  confidence?: number
}

export type EmotionConfigSection = {
  cautiousAlertnessThreshold: number
  cautiousConfidenceThreshold: number
  exploreMinCuriosity: number
  exploreMinMood: number
  restMaxAlertness: number
  restMaxMood: number
  normal: BehaviorProfileConfigSection
  explore: BehaviorProfileConfigSection
  cautious: BehaviorProfileConfigSection
  rest: BehaviorProfileConfigSection
  deltaMessageSuccess: EmotionDeltaConfigSection
  deltaMessageFailed: EmotionDeltaConfigSection
  deltaToolSuccess: EmotionDeltaConfigSection
  deltaToolError: EmotionDeltaConfigSection
  deltaUserSatisfied: EmotionDeltaConfigSection
  deltaUserDissatisfied: EmotionDeltaConfigSection
  deltaTaskCompleted: EmotionDeltaConfigSection
  deltaTaskFailed: EmotionDeltaConfigSection
  deltaPainHigh: EmotionDeltaConfigSection
  deltaPainCritical: EmotionDeltaConfigSection
}

export type SystemConfigDto = {
  agent: AgentConfigSection
  skills: SkillsConfigSection
  emotion: EmotionConfigSection
}

export async function getSystemConfig(): Promise<SystemConfigDto> {
  const { data } = await request.get<SystemConfigDto>('/api/config')
  return data
}

export async function updateAgentConfig(req: AgentConfigSection): Promise<void> {
  await request.post('/api/config/agent', req)
}

export async function updateSkillsConfig(req: SkillsConfigSection): Promise<void> {
  await request.post('/api/config/skills', req)
}

export async function updateEmotionConfig(req: EmotionConfigSection): Promise<void> {
  await request.post('/api/config/emotion', req)
}
