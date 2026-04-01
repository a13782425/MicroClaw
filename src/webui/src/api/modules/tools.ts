import request from '../request'
import type { ToolGroup } from './agents'

export type GlobalToolGroup = ToolGroup & {
  loadError?: boolean
}

export async function listAllTools(): Promise<GlobalToolGroup[]> {
  const { data } = await request.get<GlobalToolGroup[]>('/api/tools')
  return data
}