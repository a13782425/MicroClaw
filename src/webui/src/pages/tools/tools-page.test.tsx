import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import ToolsPage from '@/pages/tools'
import * as gateway from '@/api/gateway'
import type { GlobalToolGroup, McpServerConfig, ChannelToolInfo } from '@/api/gateway'

vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    listAllTools: vi.fn(),
    listMcpServers: vi.fn(),
    listMcpServerTools: vi.fn(),
    getChannelTools: vi.fn(),
  }
})

const mockGroups: GlobalToolGroup[] = [
  {
    id: 'group1',
    name: '基础工具组',
    type: 'builtin',
    isEnabled: true,
    tools: [
      { name: 'search_web', description: '搜索互联网', isEnabled: true },
      { name: 'read_file', description: '读取文件内容', isEnabled: true },
    ],
    loadError: undefined,
  },
]

const mockServers: McpServerConfig[] = [
  {
    id: 's1',
    name: '本地 MCP Server',
    transportType: 'stdio',
    command: 'python',
    args: ['-m', 'server'],
    env: {},
    isEnabled: true,
    createdAtUtc: '2024-01-01T00:00:00Z',
    source: 'manual',
  },
]

const mockChannelTools: ChannelToolInfo[] = [
  { name: 'send_message', description: '发送飞书消息' },
  { name: 'get_user_info', description: '获取用户信息' },
]

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

describe('ToolsPage', () => {
  beforeEach(() => {
    vi.mocked(gateway.listAllTools).mockResolvedValue(mockGroups)
    vi.mocked(gateway.listMcpServers).mockResolvedValue(mockServers)
    vi.mocked(gateway.listMcpServerTools).mockResolvedValue([
      { name: 'mcp_tool_1', description: 'MCP 工具 1' },
    ])
    vi.mocked(gateway.getChannelTools).mockResolvedValue(mockChannelTools)
  })

  it('默认显示内置工具 Tab', () => {
    wrap(<ToolsPage />)
    expect(screen.getByText('内置工具')).toBeInTheDocument()
    expect(screen.getByText('渠道工具')).toBeInTheDocument()
    expect(screen.getByText('MCP 工具')).toBeInTheDocument()
  })

  it('加载后显示内置工具分组', async () => {
    wrap(<ToolsPage />)
    await waitFor(() => {
      expect(screen.getByText('基础工具组')).toBeInTheDocument()
    })
  })

  it('显示工具名称和描述', async () => {
    wrap(<ToolsPage />)
    await waitFor(() => {
      expect(screen.getByText('search_web')).toBeInTheDocument()
      expect(screen.getByText('搜索互联网')).toBeInTheDocument()
      expect(screen.getByText('read_file')).toBeInTheDocument()
    })
  })

  it('点击渠道工具 Tab 后调用 getChannelTools', async () => {
    wrap(<ToolsPage />)
    fireEvent.click(screen.getByText('渠道工具'))
    await waitFor(() => {
      expect(gateway.getChannelTools).toHaveBeenCalledWith('feishu')
    })
  })

  it('渠道工具 Tab 显示工具列表', async () => {
    wrap(<ToolsPage />)
    fireEvent.click(screen.getByText('渠道工具'))
    await waitFor(() => {
      expect(screen.getByText('send_message')).toBeInTheDocument()
      expect(screen.getByText('发送飞书消息')).toBeInTheDocument()
    })
  })

  it('点击 MCP 工具 Tab 后显示服务器列表', async () => {
    wrap(<ToolsPage />)
    fireEvent.click(screen.getByText('MCP 工具'))
    await waitFor(() => {
      expect(screen.getByText('本地 MCP Server')).toBeInTheDocument()
    })
  })

  it('点击加载工具按钮调用 listMcpServerTools', async () => {
    wrap(<ToolsPage />)
    fireEvent.click(screen.getByText('MCP 工具'))
    await waitFor(() => screen.getByText('本地 MCP Server'))
    const loadBtn = screen.getByRole('button', { name: /加载工具/ })
    fireEvent.click(loadBtn)
    await waitFor(() => {
      expect(gateway.listMcpServerTools).toHaveBeenCalledWith('s1')
    })
  })
})
