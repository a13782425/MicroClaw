import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSessionStore } from '../sessionStore'
import type { SessionInfo } from '@/services/gatewayApi'

vi.mock('@/services/gatewayApi', () => ({
  listSessions: vi.fn(),
  createSession: vi.fn(),
  deleteSession: vi.fn(),
  approveSession: vi.fn(),
  disableSession: vi.fn(),
  getMessages: vi.fn(),
  streamChat: vi.fn(),
}))

async function getMocks() {
  const api = await import('@/services/gatewayApi')
  return {
    listSessions: api.listSessions as ReturnType<typeof vi.fn>,
    createSession: api.createSession as ReturnType<typeof vi.fn>,
    deleteSession: api.deleteSession as ReturnType<typeof vi.fn>,
    approveSession: api.approveSession as ReturnType<typeof vi.fn>,
    disableSession: api.disableSession as ReturnType<typeof vi.fn>,
    getMessages: api.getMessages as ReturnType<typeof vi.fn>,
  }
}

const fakeSessions: SessionInfo[] = [
  { id: 's1', title: '测试会话1', providerId: 'p1', channelId: '', isApproved: true, channelType: 'web', createdAt: '2025-01-01T00:00:00Z' },
  { id: 's2', title: '测试会话2', providerId: 'p1', channelId: '', isApproved: false, channelType: 'feishu', createdAt: '2025-01-02T00:00:00Z' },
]

describe('useSessionStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('初始状态为空', () => {
    const store = useSessionStore()
    expect(store.sessions).toEqual([])
    expect(store.currentSessionId).toBeNull()
    expect(store.messages).toEqual([])
    expect(store.chatting).toBe(false)
  })

  it('fetchSessions 加载会话列表', async () => {
    const mocks = await getMocks()
    mocks.listSessions.mockResolvedValueOnce(fakeSessions)

    const store = useSessionStore()
    await store.fetchSessions()

    expect(store.sessions).toEqual(fakeSessions)
    expect(mocks.listSessions).toHaveBeenCalledOnce()
  })

  it('addSession 创建并 prepend 会话', async () => {
    const mocks = await getMocks()
    const newSession: SessionInfo = { id: 's3', title: '新会话', providerId: 'p1', channelId: '', isApproved: true, channelType: 'web', createdAt: '2025-01-03T00:00:00Z' }
    mocks.createSession.mockResolvedValueOnce(newSession)

    const store = useSessionStore()
    store.sessions = [...fakeSessions]

    const result = await store.addSession({ title: '新会话', providerId: 'p1' })

    expect(result).toEqual(newSession)
    expect(store.sessions[0]).toEqual(newSession)
    expect(store.sessions).toHaveLength(3)
  })

  it('removeSession 删除会话并清理当前选中', async () => {
    const mocks = await getMocks()
    mocks.deleteSession.mockResolvedValueOnce(undefined)

    const store = useSessionStore()
    store.sessions = [...fakeSessions]
    store.currentSessionId = 's1'
    store.messages = [{ role: 'user', content: 'hello', timestamp: '2025-01-01T00:00:00Z' }]

    await store.removeSession('s1')

    expect(store.sessions).toHaveLength(1)
    expect(store.sessions[0].id).toBe('s2')
    expect(store.currentSessionId).toBeNull()
    expect(store.messages).toEqual([])
  })

  it('removeSession 删除非当前会话不影响选中', async () => {
    const mocks = await getMocks()
    mocks.deleteSession.mockResolvedValueOnce(undefined)

    const store = useSessionStore()
    store.sessions = [...fakeSessions]
    store.currentSessionId = 's1'

    await store.removeSession('s2')

    expect(store.sessions).toHaveLength(1)
    expect(store.currentSessionId).toBe('s1')
  })

  it('approve 更新会话审批状态', async () => {
    const mocks = await getMocks()
    const approved = { ...fakeSessions[1], isApproved: true }
    mocks.approveSession.mockResolvedValueOnce(approved)

    const store = useSessionStore()
    store.sessions = [...fakeSessions]

    await store.approve('s2')

    expect(store.sessions[1].isApproved).toBe(true)
  })

  it('selectSession 加载消息', async () => {
    const mocks = await getMocks()
    const fakeMessages = [
      { role: 'user', content: 'hi', timestamp: '2025-01-01T00:00:00Z' },
      { role: 'assistant', content: 'hello', timestamp: '2025-01-01T00:00:01Z' },
    ]
    mocks.getMessages.mockResolvedValueOnce(fakeMessages)

    const store = useSessionStore()
    await store.selectSession('s1')

    expect(store.currentSessionId).toBe('s1')
    expect(store.messages).toEqual(fakeMessages)
  })
})
