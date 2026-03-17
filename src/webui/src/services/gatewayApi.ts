import axios from 'axios'
import { useAuthStore } from '@/stores/auth'
import { router } from '@/router'

export type GatewayHealth = {
  status: string
  service: string
  utcNow: string
  version: string
}

export type ProviderProtocol = 'openai' | 'openai-responses' | 'anthropic'

export type ProviderConfig = {
  id: string
  displayName: string
  protocol: ProviderProtocol
  baseUrl: string | null
  apiKey: string
  modelName: string
  isEnabled: boolean
}

export type ProviderCreateRequest = {
  displayName: string
  protocol: ProviderProtocol
  baseUrl?: string
  apiKey: string
  modelName: string
  isEnabled: boolean
}

export type ProviderUpdateRequest = {
  id: string
  displayName?: string
  protocol?: ProviderProtocol
  baseUrl?: string
  apiKey?: string
  modelName?: string
  isEnabled: boolean
}

export type ProviderDeleteRequest = {
  id: string
}

axios.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.token) {
    config.headers.Authorization = 'Bearer ' + auth.token
  }
  return config
})

axios.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore().clearAuth()
      router.push({ name: 'login' })
    }
    return Promise.reject(error)
  }
)

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await axios.get<GatewayHealth>('/api/health')
  return data
}

export async function login(username: string, password: string) {
  const { data } = await axios.post('/api/auth/login', { username, password })
  return data as {
    token: string
    username: string
    role: string
    expiresAtUtc: string
  }
}

export async function listProviders(): Promise<ProviderConfig[]> {
  const { data } = await axios.get<ProviderConfig[]>('/api/providers')
  return data
}

export async function createProvider(req: ProviderCreateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/providers', req)
  return data
}

export async function updateProvider(req: ProviderUpdateRequest): Promise<{ id: string }> {
  const { data } = await axios.post<{ id: string }>('/api/providers/update', req)
  return data
}

export async function deleteProvider(id: string): Promise<void> {
  await axios.post('/api/providers/delete', { id })
}

// ─── Sessions ────────────────────────────────────────────────────────────────

export type MessageAttachment = {
  fileName: string
  mimeType: string
  base64Data: string
}

export type SessionMessage = {
  role: 'user' | 'assistant'
  content: string
  thinkContent?: string | null
  timestamp: string
  attachments?: MessageAttachment[] | null
}

export type SessionInfo = {
  id: string
  title: string
  providerId: string
  isApproved: boolean
  createdAt: string
}

export type CreateSessionRequest = {
  title: string
  providerId: string
}

export type ChatRequest = {
  content: string
  attachments?: MessageAttachment[]
}

export type SseChunk =
  | { type: 'token'; content: string }
  | { type: 'done'; thinkContent?: string | null }
  | { type: 'error'; message: string }

export async function listSessions(): Promise<SessionInfo[]> {
  const { data } = await axios.get<SessionInfo[]>('/api/sessions')
  return data
}

export async function createSession(req: CreateSessionRequest): Promise<SessionInfo> {
  const { data } = await axios.post<SessionInfo>('/api/sessions', req)
  return data
}

export async function deleteSession(id: string): Promise<void> {
  await axios.post('/api/sessions/delete', { id })
}

export async function approveSession(id: string): Promise<SessionInfo> {
  const { data } = await axios.post<SessionInfo>('/api/sessions/approve', { id })
  return data
}

export async function getMessages(sessionId: string): Promise<SessionMessage[]> {
  const { data } = await axios.get<SessionMessage[]>(`/api/sessions/${sessionId}/messages`)
  return data
}

/**
 * SSE 流式对话，通过 fetch 发送请求并逐 chunk 回调。
 * 返回 AbortController 以供调用方取消。
 */
export function streamChat(
  sessionId: string,
  req: ChatRequest,
  onChunk: (chunk: SseChunk) => void,
  onError: (err: string) => void,
  onDone: () => void
): AbortController {
  const controller = new AbortController()
  const auth = useAuthStore()

  ;(async () => {
    try {
      const response = await fetch(`/api/sessions/${sessionId}/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: 'Bearer ' + auth.token
        },
        body: JSON.stringify(req),
        signal: controller.signal
      })

      if (!response.ok) {
        const body = await response.json().catch(() => ({ message: response.statusText }))
        onError(body.message ?? response.statusText)
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
              onError((chunk as { type: 'error'; message: string }).message)
              return
            }
            onChunk(chunk)
          } catch {
            // 忽略非法 JSON 行
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