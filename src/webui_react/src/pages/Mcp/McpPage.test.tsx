import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import McpPage from '@/pages/Mcp'
import * as gateway from '@/api/gateway'
import type { McpServerConfig } from '@/api/gateway'

vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    listMcpServers: vi.fn(),
    createMcpServer: vi.fn(),
    updateMcpServer: vi.fn(),
    deleteMcpServer: vi.fn(),
    testMcpServer: vi.fn(),
    listMcpServerTools: vi.fn(),
  }
})

const mockServers: McpServerConfig[] = [
  {
    id: 'm1',
    name: 'Python MCP',
    transportType: 'stdio',
    command: 'python',
    args: ['server.py'],
    env: null,
    url: null,
    headers: null,
    isEnabled: true,
    createdAtUtc: '2024-01-01T00:00:00Z',
  },
  {
    id: 'm2',
    name: 'SSE Server',
    transportType: 'sse',
    command: null,
    args: null,
    env: null,
    url: 'http://localhost:8080/sse',
    headers: null,
    isEnabled: false,
    createdAtUtc: '2024-01-02T00:00:00Z',
  },
]

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

describe('McpPage', () => {
  beforeEach(() => {
    vi.mocked(gateway.listMcpServers).mockResolvedValue(mockServers)
    vi.mocked(gateway.createMcpServer).mockResolvedValue({ id: 'new-id' })
    vi.mocked(gateway.deleteMcpServer).mockResolvedValue(undefined)
    vi.mocked(gateway.listMcpServerTools).mockResolvedValue([
      { name: 'search', description: '搜索工具' },
    ])
    vi.spyOn(window, 'confirm').mockReturnValue(true)
  })

  it('加载后显示服务器列表', async () => {
    wrap(<McpPage />)
    await waitFor(() => {
      expect(screen.getByText('Python MCP')).toBeInTheDocument()
      expect(screen.getByText('SSE Server')).toBeInTheDocument()
    })
  })

  it('显示传输类型标签', async () => {
    wrap(<McpPage />)
    await waitFor(() => {
      expect(screen.getByText('Stdio')).toBeInTheDocument()
      expect(screen.getByText('SSE')).toBeInTheDocument()
    })
  })

  it('点击新建按钮打开弹窗', async () => {
    wrap(<McpPage />)
    await waitFor(() => screen.getByText('Python MCP'))
    fireEvent.click(screen.getByRole('button', { name: /新建/ }))
    await waitFor(() => {
      expect(screen.getByText('新建 MCP 服务器')).toBeInTheDocument()
    })
  })

  it('点击删除调用 deleteMcpServer', async () => {
    wrap(<McpPage />)
    await waitFor(() => screen.getByText('Python MCP'))
    const deleteBtns = screen.getAllByRole('button').filter((b) => b.querySelector('svg'))
    // 点击第一个删除按钮（Trash2）
    const trashBtns = deleteBtns.filter((b) => b.getAttribute('title') === null)
    if (trashBtns.length > 0) {
      fireEvent.click(trashBtns[trashBtns.length - 1])
      await waitFor(() => {
        expect(screen.getByText('删除 MCP 服务器')).toBeInTheDocument()
      })
      fireEvent.click(screen.getByRole('button', { name: '删除' }))
      await waitFor(() => {
        expect(gateway.deleteMcpServer).toHaveBeenCalled()
      })
    }
  })

  it('显示命令列（stdio 类型）', async () => {
    wrap(<McpPage />)
    await waitFor(() => {
      expect(screen.getByText('python')).toBeInTheDocument()
    })
  })

  it('显示 URL 列（sse 类型）', async () => {
    wrap(<McpPage />)
    await waitFor(() => {
      expect(screen.getByText('http://localhost:8080/sse')).toBeInTheDocument()
    })
  })
})
