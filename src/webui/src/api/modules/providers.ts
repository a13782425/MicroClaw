import request from '../request'

export type ProviderProtocol = 'openai' | 'anthropic'

export type ModelType = 'chat' | 'embedding'

export type InputModality = 'Text' | 'Image' | 'Audio' | 'Video' | 'File'
export type OutputModality = 'Text' | 'Image' | 'Audio' | 'Video'
export type ProviderFeature = 'FunctionCalling' | 'ResponsesApi'

export type ProviderCapabilities = {
  inputs: InputModality[]
  outputs: OutputModality[]
  features: ProviderFeature[]
  inputPricePerMToken: number | null
  outputPricePerMToken: number | null
  cacheInputPricePerMToken: number | null
  cacheOutputPricePerMToken: number | null
  notes: string | null
  qualityScore: number
  latencyTier: string
  outputDimensions: number | null
  maxInputTokens: number | null
}

export type ProviderConfig = {
  id: string
  displayName: string
  protocol: ProviderProtocol
  modelType: ModelType
  baseUrl: string | null
  apiKey: string
  modelName: string
  maxOutputTokens: number
  isEnabled: boolean
  isDefault: boolean
  capabilities: ProviderCapabilities
}

export type ProviderCreateRequest = {
  displayName: string
  protocol: ProviderProtocol
  modelType: ModelType
  baseUrl?: string
  apiKey: string
  modelName: string
  maxOutputTokens?: number
  isEnabled: boolean
  capabilities?: Partial<ProviderCapabilities>
}

export type ProviderUpdateRequest = {
  id: string
  displayName?: string
  protocol?: ProviderProtocol
  modelType?: ModelType
  baseUrl?: string
  apiKey?: string
  modelName?: string
  maxOutputTokens?: number
  isEnabled: boolean
  capabilities?: Partial<ProviderCapabilities>
}

export async function listProviders(): Promise<ProviderConfig[]> {
  const { data } = await request.get<ProviderConfig[]>('/api/providers')
  return data
}

export async function createProvider(req: ProviderCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/providers', req)
  return data
}

export async function updateProvider(req: ProviderUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/providers/update', req)
  return data
}

export async function deleteProvider(id: string): Promise<void> {
  await request.post('/api/providers/delete', { id })
}

export async function setDefaultProvider(id: string): Promise<void> {
  await request.post('/api/providers/set-default', { id })
}