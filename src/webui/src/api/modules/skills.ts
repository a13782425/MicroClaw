import request from '../request'

export type SkillConfig = {
  id: string
  name: string
  description: string
  disableModelInvocation: boolean
  userInvocable: boolean
  allowedTools: string
  model: string | null
  effort: string | null
  context: string | null
  agent: string | null
  argumentHint: string
  hooks: string
  createdAtUtc: string
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

export async function scanSkills(): Promise<{ added: number; found: number }> {
  const { data } = await request.post<{ added: number; found: number }>('/api/skills/scan')
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