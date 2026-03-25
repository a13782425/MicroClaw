import { create } from 'zustand'
import {
  listSessions,
  createSession,
  deleteSession,
  approveSession,
  disableSession,
  getMessagesPaged,
  streamChat,
  type SessionInfo,
  type SessionMessage,
  type CreateSessionRequest,
  type ChatRequest,
  type SseChunk,
  SYSTEM_SOURCES,
} from '@/api/gateway'

export type { SessionInfo, SessionMessage }

const PAGE_SIZE = 50

interface SessionState {
  sessions: SessionInfo[]
  currentSessionId: string | null
  messages: SessionMessage[]
  loading: boolean
  chatting: boolean
  streamingContent: string
  streamingThink: string
  messagesTotal: number
  messagesHasMore: boolean
  loadingEarlier: boolean

  // Actions
  fetchSessions: () => Promise<void>
  addSession: (req: CreateSessionRequest) => Promise<SessionInfo>
  removeSession: (id: string) => Promise<void>
  approve: (id: string, reason?: string) => Promise<void>
  disable: (id: string, reason?: string) => Promise<void>
  selectSession: (id: string) => Promise<void>
  loadEarlierMessages: () => Promise<void>
  currentSession: () => SessionInfo | null
  sendMessage: (req: ChatRequest) => AbortController | null
  stopChat: () => void
  clearCurrentSession: () => void
}

let abortController: AbortController | null = null

export const useSessionStore = create<SessionState>((set, get) => ({
  sessions: [],
  currentSessionId: null,
  messages: [],
  loading: false,
  chatting: false,
  streamingContent: '',
  streamingThink: '',
  messagesTotal: 0,
  messagesHasMore: false,
  loadingEarlier: false,

  currentSession: () => {
    const { sessions, currentSessionId } = get()
    return sessions.find((s) => s.id === currentSessionId) ?? null
  },

  fetchSessions: async () => {
    const data = await listSessions()
    set({ sessions: data })
  },

  addSession: async (req) => {
    const info = await createSession(req)
    set((s) => ({ sessions: [info, ...s.sessions] }))
    return info
  },

  removeSession: async (id) => {
    await deleteSession(id)
    set((s) => ({
      sessions: s.sessions.filter((x) => x.id !== id),
      ...(s.currentSessionId === id
        ? { currentSessionId: null, messages: [] }
        : {}),
    }))
  },

  approve: async (id, reason) => {
    const updated = await approveSession(id, reason)
    set((s) => ({
      sessions: s.sessions.map((x) => (x.id === id ? updated : x)),
    }))
  },

  disable: async (id, reason) => {
    const updated = await disableSession(id, reason)
    set((s) => ({
      sessions: s.sessions.map((x) => (x.id === id ? updated : x)),
    }))
  },

  selectSession: async (id) => {
    set({ currentSessionId: id, loading: true, messagesTotal: 0, messagesHasMore: false })
    try {
      const result = await getMessagesPaged(id, 0, PAGE_SIZE)
      set({
        messages: result.messages,
        messagesTotal: result.total,
        messagesHasMore: result.hasMore,
      })
    } finally {
      set({ loading: false })
    }
  },

  loadEarlierMessages: async () => {
    const { currentSessionId, messages, loadingEarlier } = get()
    if (!currentSessionId || loadingEarlier) return
    set({ loadingEarlier: true })
    try {
      const result = await getMessagesPaged(currentSessionId, messages.length, PAGE_SIZE)
      set((s) => ({
        messages: [...result.messages, ...s.messages],
        messagesHasMore: result.hasMore,
      }))
    } finally {
      set({ loadingEarlier: false })
    }
  },

  sendMessage: (req) => {
    const { currentSessionId } = get()
    if (!currentSessionId || get().chatting) return null

    const userMsg: SessionMessage = {
      role: 'user',
      content: req.content,
      timestamp: new Date().toISOString(),
      attachments: req.attachments ?? null,
    }
    set((s) => ({ messages: [...s.messages, userMsg], chatting: true, streamingContent: '', streamingThink: '' }))

    const onChunk = (chunk: SseChunk) => {
      if (chunk.type === 'token') {
        set((s) => ({ streamingContent: s.streamingContent + chunk.content }))
      }
    }

    const onError = (err: string) => {
      set({ chatting: false })
      console.error('Chat error:', err)
    }

    const onDone = () => {
      set((s) => {
        const assistantMsg: SessionMessage = {
          role: 'assistant',
          content: s.streamingContent,
          timestamp: new Date().toISOString(),
        }
        return {
          messages: [...s.messages, assistantMsg],
          chatting: false,
          streamingContent: '',
          streamingThink: '',
        }
      })
    }

    abortController = streamChat(currentSessionId, req, onChunk, onError, onDone)
    return abortController
  },

  stopChat: () => {
    abortController?.abort()
    abortController = null
    set({ chatting: false })
  },

  clearCurrentSession: () => {
    set({ currentSessionId: null, messages: [], messagesTotal: 0, messagesHasMore: false })
  },
}))

// Filters out system-triggered messages for display
export function isDisplayMessage(msg: SessionMessage): boolean {
  return !(msg.role === 'user' && msg.source && SYSTEM_SOURCES.has(msg.source))
}
