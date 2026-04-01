import request from '../request'

export type PluginSource = {
  type: 'local' | 'git'
  url?: string
  ref?: string
}

export type MarketplaceInfo = {
  name: string
  rootPath: string
  marketplaceType: string
  source: PluginSource
  registeredAt: string
}

export type MarketplacePluginSource = {
  sourceType: 'Local' | 'Url' | 'GitSubdir' | 'GitHub'
  url?: string
  path?: string
  ref?: string
  sha?: string
  repo?: string
}

export type MarketplacePluginEntry = {
  name: string
  description?: string
  category?: string
  author?: { name?: string; email?: string; url?: string }
  homepage?: string
  keywords?: string[]
  version?: string
  source: MarketplacePluginSource
  tags?: string[]
}

export async function getMarketplaces(): Promise<MarketplaceInfo[]> {
  const { data } = await request.get<MarketplaceInfo[]>('/api/marketplace')
  return data
}

export async function addMarketplace(url: string, ref?: string): Promise<MarketplaceInfo> {
  const { data } = await request.post<MarketplaceInfo>('/api/marketplace', { url, ref })
  return data
}

export async function removeMarketplace(name: string): Promise<void> {
  await request.delete(`/api/marketplace/${encodeURIComponent(name)}`)
}

export async function updateMarketplace(name: string): Promise<MarketplaceInfo> {
  const { data } = await request.post<MarketplaceInfo>(`/api/marketplace/${encodeURIComponent(name)}/update`)
  return data
}

export async function getMarketplacePlugins(
  name: string,
  keyword?: string,
  category?: string,
): Promise<MarketplacePluginEntry[]> {
  const params = new URLSearchParams()
  if (keyword) params.set('keyword', keyword)
  if (category) params.set('category', category)
  const qs = params.toString()
  const { data } = await request.get<MarketplacePluginEntry[]>(
    `/api/marketplace/${encodeURIComponent(name)}/plugins${qs ? `?${qs}` : ''}`,
  )
  return data
}