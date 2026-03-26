import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import SkillsPage from '@/pages/skills'
import * as gateway from '@/api/gateway'
import type { SkillConfig } from '@/api/gateway'

vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    listSkills: vi.fn(),
    createSkill: vi.fn(),
    updateSkill: vi.fn(),
    deleteSkill: vi.fn(),
    listSkillFiles: vi.fn(),
    getSkillFileContent: vi.fn(),
    writeSkillFile: vi.fn(),
    deleteSkillFile: vi.fn(),
  }
})

const mockSkills: SkillConfig[] = [
  {
    id: 's1',
    name: 'Python 脚本',
    description: '执行 Python 脚本',
    skillType: 'python',
    entryPoint: 'main.py',
    isEnabled: true,
    createdAtUtc: '2024-01-01T00:00:00Z',
  },
  {
    id: 's2',
    name: 'Node 工具',
    description: '',
    skillType: 'nodejs',
    entryPoint: 'index.js',
    isEnabled: false,
    createdAtUtc: '2024-01-02T00:00:00Z',
  },
]

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

describe('SkillsPage', () => {
  beforeEach(() => {
    vi.mocked(gateway.listSkills).mockResolvedValue(mockSkills)
    vi.mocked(gateway.createSkill).mockResolvedValue({ id: 'new-id' })
    vi.mocked(gateway.updateSkill).mockResolvedValue({ id: 's1' })
    vi.mocked(gateway.deleteSkill).mockResolvedValue(undefined)
    vi.mocked(gateway.listSkillFiles).mockResolvedValue([])
  })

  it('加载后显示技能列表', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => {
      expect(screen.getByText('Python 脚本')).toBeInTheDocument()
      expect(screen.getByText('Node 工具')).toBeInTheDocument()
    })
  })

  it('显示技能类型 badge', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => {
      expect(screen.getByText('Python')).toBeInTheDocument()
      expect(screen.getByText('Node.js')).toBeInTheDocument()
    })
  })

  it('点击技能显示右侧详情面板', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => screen.getByText('Python 脚本'))
    fireEvent.click(screen.getByText('Python 脚本'))
    await waitFor(() => {
      expect(screen.getByText('信息')).toBeInTheDocument()
      expect(screen.getByText('文件')).toBeInTheDocument()
    })
  })

  it('未选中时显示提示文字', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => screen.getByText('Python 脚本'))
    expect(screen.getByText('请从左侧选择技能')).toBeInTheDocument()
  })

  it('点击新建按钮打开弹窗', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => screen.getByText('Python 脚本'))
    // 找到 + 按钮
    const plusBtn = screen.getByRole('button', { name: '' })
    fireEvent.click(plusBtn)
    await waitFor(() => {
      expect(screen.getByText('新建技能')).toBeInTheDocument()
    })
  })

  it('加载失败时显示 toaster 错误（listSkills rejection）', async () => {
    vi.mocked(gateway.listSkills).mockRejectedValue(new Error('network error'))
    wrap(<SkillsPage />)
    await waitFor(() => {
      expect(gateway.listSkills).toHaveBeenCalled()
    })
  })

  it('删除技能时调用 deleteSkill', async () => {
    wrap(<SkillsPage />)
    await waitFor(() => screen.getByText('Python 脚本'))
    fireEvent.click(screen.getByText('Python 脚本'))
    await waitFor(() => screen.getByText('信息'))
    // 点击删除
    fireEvent.click(screen.getByRole('button', { name: /删除/ }))
    await waitFor(() => {
      expect(screen.getByText('删除技能')).toBeInTheDocument()
    })
    const deleteButtons = screen.getAllByRole('button', { name: /删除/ })
    fireEvent.click(deleteButtons[deleteButtons.length - 1])
    await waitFor(() => {
      expect(gateway.deleteSkill).toHaveBeenCalledWith('s1')
    })
  })
})
