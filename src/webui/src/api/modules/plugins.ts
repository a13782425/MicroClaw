import request from '../request'
import type { MarketplaceInfo, PluginSource } from './marketplace'

export type { PluginSource } from './marketplace'

export type PluginSummary = {
  name: string
  isEnabled: boolean
  source: PluginSource
  installedAt: string
  description?: string
  version?: string
  author?: string
  skillCount: number
  agentCount: number
  hookCount: number
  hasMcpConfig: boolean
}

export type PluginDetail = {
  name: string
  rootPath: string
  isEnabled: boolean
  source: PluginSource
  installedAt: string
  manifest?: {
    name: string
    version?: string
    description?: string
    author?: { name?: string; email?: string; url?: string }
    keywords?: string[]
  }
  skillPaths: string[]
  agentPaths: string[]
  hooks: {
    event: string
    matcher?: string
    type: string
    command?: string
    url?: string
  }[]
  mcpConfigPath?: string
}

export type InstallPluginResult =
  | { type: 'plugin'; plugin: PluginDetail }
  | { type: 'marketplace'; marketplace: MarketplaceInfo }

export async function getPlugins(): Promise<PluginSummary[]> {
  const { data } = await request.get<PluginSummary[]>('/api/plugins')
  return data
}

export async function getPlugin(name: string): Promise<PluginDetail> {
  const { data } = await request.get<PluginDetail>(`/api/plugins/${name}`)
  return data
}

export async function installPlugin(url: string, ref?: string): Promise<InstallPluginResult> {
  const { data } = await request.post<InstallPluginResult>('/api/plugins/install', { url, ref })
  return data
}

export async function enablePlugin(name: string): Promise<void> {
  await request.post(`/api/plugins/${name}/enable`)
}

export async function disablePlugin(name: string): Promise<void> {
  await request.post(`/api/plugins/${name}/disable`)
}

export async function updatePlugin(name: string): Promise<void> {
  await request.post(`/api/plugins/${name}/update`)
}

export async function uninstallPlugin(name: string): Promise<void> {
  await request.delete(`/api/plugins/${name}`)
}

export async function reloadPlugins(): Promise<void> {
  await request.post('/api/plugins/reload')
}

export async function installMarketplacePlugin(
  marketplaceName: string,
  pluginName: string,
): Promise<PluginDetail> {
  const { data } = await request.post<PluginDetail>(
    `/api/marketplace/${encodeURIComponent(marketplaceName)}/plugins/${encodeURIComponent(pluginName)}/install`,
  )
  return data
}