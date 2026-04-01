import request from '../request'

export type AgentConfigSection = {
  subAgentMaxDepth: number
}

export type SkillsConfigSection = {
  additionalFolders: string[]
}

export type SystemConfigDto = {
  agent: AgentConfigSection
  skills: SkillsConfigSection
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