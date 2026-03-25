import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import CronPage from '@/pages/Cron'
import * as cronModule from '@/api/cron'
import * as gateway from '@/api/gateway'
import type { CronJob } from '@/api/cron'

vi.mock('@/api/cron', async (importOriginal) => {
  const actual = await importOriginal<typeof cronModule>()
  return {
    ...actual,
    cronApi: {
      list: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      toggle: vi.fn(),
      trigger: vi.fn(),
      getLogs: vi.fn(),
    },
  }
})

vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    listSessions: vi.fn(),
  }
})

vi.mock('@/services/eventBus', () => ({
  eventBus: { on: vi.fn(), off: vi.fn(), emit: vi.fn() },
}))

const mockJobs: CronJob[] = [
  {
    id: 'c1',
    name: '每日摘要',
    description: '发送每日工作摘要',
    cronExpression: '0 9 * * *',
    targetSessionId: 'sess1',
    prompt: '请总结今日工作',
    isEnabled: true,
    createdAtUtc: '2024-01-01T00:00:00Z',
    lastRunAtUtc: '2024-01-10T09:00:00Z',
  },
]

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

describe('CronPage', () => {
  beforeEach(() => {
    vi.mocked(cronModule.cronApi.list).mockResolvedValue(mockJobs)
    vi.mocked(gateway.listSessions).mockResolvedValue([
      { id: 'sess1', title: '测试会话', providerId: 'p1', isApproved: true, channelType: 'web', channelId: '', createdAt: '' },
    ])
    vi.mocked(cronModule.cronApi.trigger).mockResolvedValue({ success: true, status: 'success', durationMs: 100, errorMessage: null })
    vi.mocked(cronModule.cronApi.delete).mockResolvedValue(undefined)
    vi.mocked(cronModule.cronApi.toggle).mockResolvedValue({ ...mockJobs[0], isEnabled: false })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
  })

  it('加载后显示任务列表', async () => {
    wrap(<CronPage />)
    await waitFor(() => {
      expect(screen.getByText('每日摘要')).toBeInTheDocument()
    })
  })

  it('显示 Cron 表达式', async () => {
    wrap(<CronPage />)
    await waitFor(() => {
      expect(screen.getByText('0 9 * * *')).toBeInTheDocument()
    })
  })

  it('显示描述文字', async () => {
    wrap(<CronPage />)
    await waitFor(() => {
      expect(screen.getByText('发送每日工作摘要')).toBeInTheDocument()
    })
  })

  it('新建任务按钮显示', async () => {
    wrap(<CronPage />)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /新建任务/ })).toBeInTheDocument()
    })
  })

  it('加载时调用 cronApi.list 和 listSessions', async () => {
    wrap(<CronPage />)
    await waitFor(() => {
      expect(cronModule.cronApi.list).toHaveBeenCalled()
      expect(gateway.listSessions).toHaveBeenCalled()
    })
  })
})
