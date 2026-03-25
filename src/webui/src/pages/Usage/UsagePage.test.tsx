import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import UsagePage from '@/pages/Usage'
import * as gateway from '@/api/gateway'
import type { UsageQueryResult } from '@/api/gateway'

// recharts 需要 ResizeObserver
global.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// 在 JSDOM 中 recharts 无法渲染 SVG 文字，mock 掉以简化测试
vi.mock('recharts', () => ({
  ResponsiveContainer: ({ children }: any) => <div>{children}</div>,
  AreaChart: ({ children }: any) => <div data-testid="area-chart">{children}</div>,
  LineChart: ({ children }: any) => <div data-testid="line-chart">{children}</div>,
  BarChart: ({ children, data }: any) => (
    <div data-testid="bar-chart">
      {data?.map((d: any, i: number) => <span key={i}>{d.providerName ?? d.source}</span>)}
      {children}
    </div>
  ),
  PieChart: ({ children }: any) => <div data-testid="pie-chart">{children}</div>,
  Pie: () => null,
  Sector: () => null,
  Area: () => null,
  Line: () => null,
  Bar: () => null,
  XAxis: () => null,
  YAxis: () => null,
  CartesianGrid: () => null,
  Tooltip: () => null,
  Legend: () => null,
}))

vi.mock('@chakra-ui/charts', () => ({
  Chart: {
    Root: ({ children }: any) => <div>{children}</div>,
    Tooltip: () => <div />,
    Legend: () => <div />,
    Gradient: () => null,
  },
  useChart: ({ data, series = [] }: any) => ({
    data,
    series,
    key: (name: string) => name,
    color: (name: string) => name,
    getTotal: (name: string) => data.reduce((total: number, item: Record<string, number>) => total + Number(item[name] ?? 0), 0),
  }),
}))

vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    fetchUsageStats: vi.fn(),
  }
})

const mockResult: UsageQueryResult = {
  summary: {
    totalInputTokens: 1_500_000,
    totalOutputTokens: 750_000,
    totalCostUsd: 4.2,
  },
  daily: [
    { date: '2024-01-01', inputTokens: 100000, outputTokens: 50000, estimatedCostUsd: 0.5 },
    { date: '2024-01-02', inputTokens: 200000, outputTokens: 100000, estimatedCostUsd: 1.0 },
  ],
  byProvider: [
    { providerId: 'p1', providerName: 'GPT-4o', inputTokens: 900000, outputTokens: 450000, estimatedCostUsd: 2.0 },
  ],
  bySource: [
    { source: 'web', inputTokens: 800000, outputTokens: 400000 },
    { source: 'feishu', inputTokens: 700000, outputTokens: 350000 },
  ],
  dailyByProvider: [
    { date: '2024-01-01', providerId: 'p1', providerName: 'GPT-4o', estimatedCostUsd: 0.3 },
    { date: '2024-01-02', providerId: 'p1', providerName: 'GPT-4o', estimatedCostUsd: 0.7 },
  ],
}

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

function toLocalIsoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

describe('UsagePage', () => {
  beforeEach(() => {
    vi.mocked(gateway.fetchUsageStats).mockResolvedValue(mockResult)
  })

  it('加载后显示统计卡片', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      // 统计卡片与表格表头都含有"输入 Token"，用 getAllByText 取第一个
      expect(screen.getAllByText('输入 Token')[0]).toBeInTheDocument()
      expect(screen.getAllByText('输出 Token')[0]).toBeInTheDocument()
      expect(screen.getByText('总 Token')).toBeInTheDocument()
      expect(screen.getByText('估算费用（USD）')).toBeInTheDocument()
    })
  })

  it('显示格式化后的 Token 数值', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      // 1.5M tokens
      expect(screen.getByText('1.50M')).toBeInTheDocument()
    })
  })

  it('显示按来源明细表格', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      expect(screen.getByText('按来源明细')).toBeInTheDocument()
      expect(screen.getAllByText('web').length).toBeGreaterThan(0)
      expect(screen.getAllByText('feishu').length).toBeGreaterThan(0)
    })
  })

  it('显示查询按钮', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /查询/ })).toBeInTheDocument()
    })
  })

  it('点击查询按钮调用 fetchUsageStats', async () => {
    wrap(<UsagePage />)
    // 等待初始加载完成后再点击查询
    await waitFor(() => expect(screen.getAllByText('输入 Token')[0]).toBeInTheDocument())
    const callsBefore = vi.mocked(gateway.fetchUsageStats).mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: /查询/ }))
    await waitFor(() => {
      expect(vi.mocked(gateway.fetchUsageStats).mock.calls.length).toBeGreaterThan(callsBefore)
    })
  })

  it('初始加载使用本地日期范围', async () => {
    wrap(<UsagePage />)

    await waitFor(() => {
      expect(vi.mocked(gateway.fetchUsageStats)).toHaveBeenCalled()
    })

    const now = new Date()
    const start = new Date(now)
    start.setDate(now.getDate() - 29)

    expect(vi.mocked(gateway.fetchUsageStats)).toHaveBeenCalledWith(
      toLocalIsoDate(start),
      toLocalIsoDate(now),
    )
  })

  it('日期筛选控件具有可访问名称', async () => {
    wrap(<UsagePage />)

    await waitFor(() => {
      expect(screen.getByRole('textbox', { name: '开始日期' })).toBeInTheDocument()
      expect(screen.getByRole('textbox', { name: '结束日期' })).toBeInTheDocument()
    })
  })

  it('显示 Provider 名称（柱状图）', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      expect(screen.getByText('GPT-4o')).toBeInTheDocument()
    })
  })

  it('显示 Source 名称（柱状图）', async () => {
    wrap(<UsagePage />)
    await waitFor(() => {
      expect(screen.getByText('按 Source 分组（绝对量）')).toBeInTheDocument()
      expect(screen.getAllByText('web').length).toBeGreaterThan(0)
      expect(screen.getAllByText('feishu').length).toBeGreaterThan(0)
    })
  })
})
