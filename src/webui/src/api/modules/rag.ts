import request from '../request'

export interface RagChunkInfo {
  id: string
  sourceId: string
  content: string
  hitCount: number
  createdAtMs: number
  lastAccessedAtMs: number | null
}

export async function listSessionRagChunks(sessionId: string): Promise<RagChunkInfo[]> {
  const { data } = await request.get<{ chunks: RagChunkInfo[] }>(`/api/sessions/${sessionId}/rag/chunks`)
  return data.chunks
}

export async function listGlobalRagChunks(): Promise<RagChunkInfo[]> {
  const { data } = await request.get<{ chunks: RagChunkInfo[] }>('/api/rag/global/chunks')
  return data.chunks
}

export async function deleteRagChunk(chunkId: string, scope: 'Global' | 'Session', sessionId?: string): Promise<void> {
  const params = new URLSearchParams({ scope })
  if (sessionId) params.set('sessionId', sessionId)
  await request.delete(`/api/rag/chunks/${encodeURIComponent(chunkId)}?${params}`)
}

export async function updateRagChunkHitCount(
  chunkId: string,
  hitCount: number,
  scope: 'Global' | 'Session',
  sessionId?: string,
): Promise<void> {
  const params = new URLSearchParams({ scope })
  if (sessionId) params.set('sessionId', sessionId)
  await request.post(`/api/rag/chunks/${encodeURIComponent(chunkId)}/hit-count?${params}`, { hitCount })
}

export type RagDocumentInfo = {
  sourceId: string
  fileName: string
  chunkCount: number
  indexedAtMs: number
}

export type UploadRagDocumentResult = {
  success: boolean
  sourceId: string
  fileName: string
  chunkCount: number
}

export async function listRagGlobalDocuments(): Promise<RagDocumentInfo[]> {
  const { data } = await request.get<RagDocumentInfo[]>('/api/rag/global/documents')
  return data
}

export async function uploadRagGlobalDocument(file: File): Promise<UploadRagDocumentResult> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await request.post<UploadRagDocumentResult>(
    '/api/rag/global/documents/upload',
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}

export async function deleteRagGlobalDocument(sourceId: string): Promise<void> {
  await request.post('/api/rag/global/documents/delete', { sourceId })
}

export async function reindexRagGlobalDocument(sourceId: string): Promise<{ success: boolean; chunkCount: number }> {
  const { data } = await request.post<{ success: boolean; chunkCount: number }>(
    '/api/rag/global/documents/reindex',
    { sourceId },
  )
  return data
}

export type RagQueryStats = {
  scope: string
  totalQueries: number
  hitQueries: number
  hitRate: number
  avgElapsedMs: number
  avgRecallCount: number
  last24hQueries: number
}

export async function getRagQueryStats(scope?: 'Global' | 'Session'): Promise<RagQueryStats> {
  const params = scope ? { scope } : {}
  const { data } = await request.get<RagQueryStats>('/api/rag/stats', { params })
  return data
}

export type RagConfig = {
  maxStorageSizeMb: number
  pruneTargetPercent: number
}

export async function getRagConfig(): Promise<RagConfig> {
  const { data } = await request.get<RagConfig>('/api/rag/config')
  return data
}

export async function updateRagConfig(config: RagConfig): Promise<{ success: boolean }> {
  const { data } = await request.post<{ success: boolean }>('/api/rag/config', config)
  return data
}

export type RagReindexStatus = {
  status: 'idle' | 'running' | 'done' | 'error'
  total: number
  completed: number
  currentItem: string | null
  error: string | null
}

export async function startRagReindexAll(): Promise<{ started: boolean }> {
  const { data } = await request.post<{ started: boolean }>('/api/rag/reindex-all')
  return data
}

export async function getRagReindexStatus(): Promise<RagReindexStatus> {
  const { data } = await request.get<RagReindexStatus>('/api/rag/reindex-all/status')
  return data
}