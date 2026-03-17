import { ref } from 'vue'
import { defineStore } from 'pinia'
import {
  listSessions,
  createSession,
  deleteSession,
  approveSession,
  getMessages,
  streamChat,
  type SessionInfo,
  type SessionMessage,
  type CreateSessionRequest,
  type ChatRequest,
  type SseChunk,
  type MessageAttachment
} from '@/services/gatewayApi'

export type { SessionInfo, SessionMessage, MessageAttachment }

export const useSessionStore = defineStore('session', () => {
  const sessions = ref<SessionInfo[]>([])
  const currentSessionId = ref<string | null>(null)
  const messages = ref<SessionMessage[]>([])
  const loading = ref(false)
  const chatting = ref(false)
  const streamingContent = ref('')
  const streamingThink = ref('')

  let abortController: AbortController | null = null

  const currentSession = () =>
    sessions.value.find((s) => s.id === currentSessionId.value) ?? null

  async function fetchSessions() {
    sessions.value = await listSessions()
  }

  async function addSession(req: CreateSessionRequest): Promise<SessionInfo> {
    const info = await createSession(req)
    sessions.value.unshift(info)
    return info
  }

  async function removeSession(id: string) {
    await deleteSession(id)
    sessions.value = sessions.value.filter((s) => s.id !== id)
    if (currentSessionId.value === id) {
      currentSessionId.value = null
      messages.value = []
    }
  }

  async function approve(id: string) {
    const updated = await approveSession(id)
    const idx = sessions.value.findIndex((s) => s.id === id)
    if (idx >= 0) sessions.value[idx] = updated
  }

  async function selectSession(id: string) {
    currentSessionId.value = id
    loading.value = true
    try {
      messages.value = await getMessages(id)
    } finally {
      loading.value = false
    }
  }

  async function sendMessage(content: string, attachments?: MessageAttachment[]) {
    const id = currentSessionId.value
    if (!id || chatting.value) return

    const userMsg: SessionMessage = {
      role: 'user',
      content,
      timestamp: new Date().toISOString(),
      attachments: attachments ?? null
    }
    messages.value.push(userMsg)

    streamingContent.value = ''
    streamingThink.value = ''
    chatting.value = true

    const assistantMsg: SessionMessage = {
      role: 'assistant',
      content: '',
      timestamp: new Date().toISOString()
    }
    messages.value.push(assistantMsg)
    const assistantIdx = messages.value.length - 1

    const req: ChatRequest = { content, attachments }

    abortController = streamChat(
      id,
      req,
      (chunk: SseChunk) => {
        if (chunk.type === 'token') {
          streamingContent.value += chunk.content
          messages.value[assistantIdx] = {
            ...messages.value[assistantIdx],
            content: streamingContent.value
          }
        } else if (chunk.type === 'done') {
          if (chunk.thinkContent) {
            streamingThink.value = chunk.thinkContent
            messages.value[assistantIdx] = {
              ...messages.value[assistantIdx],
              thinkContent: chunk.thinkContent
            }
          }
        }
      },
      (err: string) => {
        messages.value[assistantIdx] = {
          ...messages.value[assistantIdx],
          content: `> 错误：${err}`
        }
        chatting.value = false
        abortController = null
      },
      () => {
        chatting.value = false
        abortController = null
        streamingContent.value = ''
      }
    )
  }

  function abortChat() {
    abortController?.abort()
    abortController = null
    chatting.value = false
  }

  return {
    sessions,
    currentSessionId,
    messages,
    loading,
    chatting,
    streamingContent,
    currentSession,
    fetchSessions,
    addSession,
    removeSession,
    approve,
    selectSession,
    sendMessage,
    abortChat
  }
})
