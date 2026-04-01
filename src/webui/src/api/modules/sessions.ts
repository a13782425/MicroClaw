import request from '../request'
import { useAuthStore } from '@/store/authStore'
import type { ChannelType, SseChunk } from './shared'

export type { ChannelType, SseChunk } from './shared'

export type MessageAttachment = {
  fileName: string
  mimeType: string
  base64Data: string
}

export type MessageSource = 'cron' | 'skill' | 'tool'

export const SYSTEM_SOURCES = new Set<MessageSource>(['cron', 'skill', 'tool'])

export type MessageType =
  | 'text'
  | 'tool_call'
  | 'tool_result'
  | 'sub_agent_start'
  | 'sub_agent_result'
  | 'skill'
  | 'memory_read'
  | 'memory_write'
  | 'status'

export type SessionMessage = {
  id?: string
  role: 'user' | 'assistant' | 'tool' | 'system'
  content: string
  thinkContent?: string | null
  timestamp: string
  attachments?: MessageAttachment[] | null
  source?: MessageSource | null
  messageType?: MessageType | null
  metadata?: Record<string, unknown> | null
  visibility?: string | null
}

export type SessionInfo = {
  id: string
  title: string
  providerId: string
  isApproved: boolean
  channelType: ChannelType
  channelId: string
  createdAt: string
  agentId?: string | null
  parentSessionId?: string | null
  approvalReason?: string | null
}

export type CreateSessionRequest = {
  title: string
  providerId: string
  channelId?: string
  agentId?: string
}

export type ChatRequest = {
  content: string
  attachments?: MessageAttachment[]
}

export interface PagedMessagesResponse {
  messages: SessionMessage[]
  total: number
  hasMore: boolean
}

export async function listSessions(): Promise<SessionInfo[]> {
  const { data } = await request.get<SessionInfo[]>('/api/sessions')
  return data
}

export async function createSession(req: CreateSessionRequest): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions', req)
  return data
}

export async function deleteSession(id: string): Promise<void> {
  await request.post('/api/sessions/delete', { id })
}

export async function approveSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions/approve', { id, reason })
  return data
}

export async function disableSession(id: string, reason?: string): Promise<SessionInfo> {
  const { data } = await request.post<SessionInfo>('/api/sessions/disable', { id, reason })
  return data
}

export async function getMessagesPaged(
  sessionId: string,
  skip: number,
  limit: number,
): Promise<PagedMessagesResponse> {
  const { data } = await request.get<PagedMessagesResponse>(
    `/api/sessions/${sessionId}/messages`,
    { params: { skip, limit } },
  )
  return data
}

export async function switchSessionProvider(id: string, providerId: string): Promise<void> {
  await request.post('/api/sessions/switch-provider', { id, providerId })
}

export function streamChat(
  sessionId: string,
  req: ChatRequest,
  onChunk: (chunk: SseChunk) => void,
  onError: (err: string) => void,
  onDone: () => void,
): AbortController {
  const controller = new AbortController()
  const token = useAuthStore.getState().token

  ;(async () => {
    try {
      const response = await fetch(`/api/sessions/${sessionId}/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(req),
        signal: controller.signal,
      })

      if (!response.ok) {
        const body = await response.json().catch(() => ({ message: response.statusText }))
        onError((body as { message?: string }).message ?? response.statusText)
        return
      }

      const reader = response.body!.getReader()
      const decoder = new TextDecoder()
      let buffer = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          const trimmed = line.trim()
          if (!trimmed.startsWith('data:')) continue
          const raw = trimmed.slice(5).trim()
          if (raw === '[DONE]') {
            onDone()
            return
          }
          try {
            const chunk = JSON.parse(raw) as SseChunk
            if (chunk.type === 'error') {
              onError(chunk.message)
              return
            }
            onChunk(chunk)
          } catch {
            // Ignore malformed SSE payloads.
          }
        }
      }
      onDone()
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') return
      onError(String(err))
    }
  })()

  return controller
}

export type SessionDnaFileInfo = {
  fileName: string
  description: string
  content: string
  updatedAt: string
}

export type FeishuDocImportResult = {
  success: boolean
  file: SessionDnaFileInfo
  charCount: number
}

export async function listSessionDna(sessionId: string): Promise<SessionDnaFileInfo[]> {
  const { data } = await request.get<SessionDnaFileInfo[]>(`/api/sessions/${sessionId}/dna`)
  return data
}

export async function getSessionDnaFile(sessionId: string, fileName: string): Promise<SessionDnaFileInfo> {
  const { data } = await request.get<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna/${fileName}`)
  return data
}

export async function updateSessionDna(
  sessionId: string,
  fileName: string,
  content: string,
): Promise<SessionDnaFileInfo> {
  const { data } = await request.post<SessionDnaFileInfo>(`/api/sessions/${sessionId}/dna`, {
    fileName,
    content,
  })
  return data
}

export async function importSessionDnaFromFeishu(
  sessionId: string,
  docUrlOrToken: string,
  fileName: string,
): Promise<FeishuDocImportResult> {
  const { data } = await request.post<FeishuDocImportResult>(
    `/api/sessions/${sessionId}/dna/import-from-feishu`,
    { docUrlOrToken, fileName },
  )
  return data
}

export async function getSessionMemory(sessionId: string): Promise<string> {
  const { data } = await request.get<{ content: string }>(`/api/sessions/${sessionId}/memory`)
  return data.content
}

export async function updateSessionMemory(sessionId: string, content: string): Promise<string> {
  const { data } = await request.post<{ content: string }>(`/api/sessions/${sessionId}/memory`, {
    content,
  })
  return data.content
}

export type SessionRagStatus = {
  sessionId: string
  categoryCount: number
  lastUpdatedAtMs: number | null
}

export async function getSessionRagStatus(sessionId: string): Promise<SessionRagStatus> {
  const { data } = await request.get<SessionRagStatus>(`/api/sessions/${sessionId}/rag/status`)
  return data
}

export type VectorizeResult = {
  success: boolean
  messageCount: number
  pendingFile: string
}

export async function vectorizeSessionMessages(sessionId: string): Promise<VectorizeResult> {
  const { data } = await request.post<VectorizeResult>(`/api/sessions/${sessionId}/rag/vectorize`)
  return data
}