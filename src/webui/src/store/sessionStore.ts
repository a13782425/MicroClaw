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
  type MessageType,
  SYSTEM_SOURCES,
} from '@/api/gateway'

export type { SessionInfo, SessionMessage }

const PAGE_SIZE = 50

// ─── Tree structure ──────────────────────────────────────────────────────────
export type SessionTreeNode = {
  session: SessionInfo
  children: SessionTreeNode[]
}

export function isSubAgentSession(session: SessionInfo): boolean {
  return !!session.parentSessionId
}

export function buildSessionTree(sessions: readonly SessionInfo[]): SessionTreeNode[] {
  const nodeMap = new Map<string, SessionTreeNode>()
  for (const s of sessions) {
    nodeMap.set(s.id, { session: s, children: [] })
  }
  const roots: SessionTreeNode[] = []
  for (const s of sessions) {
    const node = nodeMap.get(s.id)!
    const parentId = s.parentSessionId
    if (parentId && nodeMap.has(parentId)) {
      nodeMap.get(parentId)!.children.push(node)
    } else {
      roots.push(node)
    }
  }
  return roots
}

interface SessionState {
  sessions: SessionInfo[]
  currentSessionId: string | null
  messages: SessionMessage[]
  loading: boolean
  chatting: boolean
  streamingContent: string
  streamingThink: string
  /** 子代理执行进度步骤，按 runId 分组 */
  subAgentProgress: Record<string, string[]>
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
  subAgentProgress: {},
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
      id: crypto.randomUUID(),
      role: 'user',
      content: req.content,
      timestamp: new Date().toISOString(),
      attachments: req.attachments ?? null,
    }
    set((s) => ({ messages: [...s.messages, userMsg], chatting: true, streamingContent: '', streamingThink: '' }))

    let currentStreamMessageId: string | undefined

    const onChunk = (chunk: SseChunk) => {
      if ('messageId' in chunk && chunk.messageId) {
        currentStreamMessageId = chunk.messageId
      }

      if (chunk.type === 'token') {
        set((s) => ({ streamingContent: s.streamingContent + chunk.content }))
      } else if (chunk.type === 'done') {
        if (chunk.thinkContent) {
          set({ streamingThink: chunk.thinkContent })
        }
      } else if (chunk.type === 'tool_call') {
        const msg: SessionMessage = {
          id: currentStreamMessageId ?? crypto.randomUUID(),
          role: 'assistant',
          content: `调用工具: ${chunk.toolName}`,
          timestamp: new Date().toISOString(),
          messageType: 'tool_call',
          metadata: { callId: chunk.callId, toolName: chunk.toolName, arguments: chunk.arguments },
        }
        set((s) => ({ messages: [...s.messages, msg] }))
      } else if (chunk.type === 'tool_result') {
        const msg: SessionMessage = {
          id: currentStreamMessageId ?? crypto.randomUUID(),
          role: 'tool',
          content: chunk.result,
          timestamp: new Date().toISOString(),
          messageType: 'tool_result',
          metadata: { callId: chunk.callId, toolName: chunk.toolName, success: chunk.success, durationMs: chunk.durationMs },
        }
        set((s) => ({ messages: [...s.messages, msg] }))
      } else if (chunk.type === 'sub_agent_start') {
        const msg: SessionMessage = {
          id: currentStreamMessageId ?? crypto.randomUUID(),
          role: 'system',
          content: `子代理 ${chunk.agentName} 开始执行`,
          timestamp: new Date().toISOString(),
          messageType: 'sub_agent_start',
          metadata: { agentId: chunk.agentId, agentName: chunk.agentName, task: chunk.task, runId: chunk.runId },
        }
        set((s) => ({ messages: [...s.messages, msg] }))
      } else if (chunk.type === 'sub_agent_done') {
        const msg: SessionMessage = {
          id: currentStreamMessageId ?? crypto.randomUUID(),
          role: 'system',
          content: chunk.result,
          timestamp: new Date().toISOString(),
          messageType: 'sub_agent_result',
          metadata: { agentId: chunk.agentId, agentName: chunk.agentName, durationMs: chunk.durationMs, runId: chunk.runId },
        }
        set((s) => {
          const { [chunk.runId]: _, ...rest } = s.subAgentProgress
          return { messages: [...s.messages, msg], subAgentProgress: rest }
        })
      } else if (chunk.type === 'sub_agent_progress') {
        set((s) => ({
          subAgentProgress: {
            ...s.subAgentProgress,
            [chunk.runId]: [...(s.subAgentProgress[chunk.runId] ?? []), chunk.step],
          },
        }))
      }
    }

    const onError = (err: string) => {
      set({ chatting: false })
      console.error('Chat error:', err)
    }

    const onDone = () => {
      set((s) => {
        const assistantMsg: SessionMessage = {
          id: currentStreamMessageId ?? crypto.randomUUID(),
          role: 'assistant',
          content: s.streamingContent,
          thinkContent: s.streamingThink || null,
          timestamp: new Date().toISOString(),
        }
        return {
          messages: [...s.messages, assistantMsg],
          chatting: false,
          streamingContent: '',
          streamingThink: '',
          subAgentProgress: {},
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

// Filters out system-triggered user messages for display.
// All tool/sub-agent/system messages are always shown.
export function isDisplayMessage(msg: SessionMessage): boolean {
  return !(msg.role === 'user' && msg.source && SYSTEM_SOURCES.has(msg.source))
}

// Message type categories for filtering
const TOOL_TYPES: ReadonlySet<MessageType> = new Set<MessageType>(['tool_call', 'tool_result'])
const AGENT_TYPES: ReadonlySet<MessageType> = new Set<MessageType>(['sub_agent_start', 'sub_agent_result'])

export function isToolMessage(msg: SessionMessage): boolean {
  return !!msg.messageType && TOOL_TYPES.has(msg.messageType)
}

export function isSubAgentMessage(msg: SessionMessage): boolean {
  return !!msg.messageType && AGENT_TYPES.has(msg.messageType)
}
