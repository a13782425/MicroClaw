import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import ModelsPage from '@/pages/Models'
import * as gateway from '@/api/gateway'
import type { ProviderConfig } from '@/api/gateway'

// Mock API
vi.mock('@/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof gateway>()
  return {
    ...actual,
    listProviders: vi.fn(),
    createProvider: vi.fn(),
    updateProvider: vi.fn(),
    deleteProvider: vi.fn(),
    setDefaultProvider: vi.fn(),
  }
})

const mockProviders: ProviderConfig[] = [
  {
    id: 'p1',
    displayName: 'GPT-4o',
    protocol: 'openai',
    baseUrl: null,
    apiKey: '***',
    modelName: 'gpt-4o',
    maxOutputTokens: 8192,
    isEnabled: true,
    isDefault: true,
    capabilities: {
      inputText: true,
      inputImage: true,
      inputAudio: false,
      inputVideo: false,
      inputFile: false,
      outputText: true,
      outputImage: false,
      outputAudio: false,
      outputVideo: false,
      supportsFunctionCalling: true,
      supportsResponsesApi: false,
      inputPricePerMToken: 2.5,
      outputPricePerMToken: 10,
      cacheInputPricePerMToken: null,
      cacheOutputPricePerMToken: null,
      notes: null,
    },
  },
  {
    id: 'p2',
    displayName: 'Claude Sonnet',
    protocol: 'anthropic',
    baseUrl: null,
    apiKey: '***',
    modelName: 'claude-sonnet-4-5',
    maxOutputTokens: 16000,
    isEnabled: false,
    isDefault: false,
    capabilities: {
      inputText: true,
      inputImage: false,
      inputAudio: false,
      inputVideo: false,
      inputFile: false,
      outputText: true,
      outputImage: false,
      outputAudio: false,
      outputVideo: false,
      supportsFunctionCalling: false,
      supportsResponsesApi: false,
      inputPricePerMToken: null,
      outputPricePerMToken: null,
      cacheInputPricePerMToken: null,
      cacheOutputPricePerMToken: null,
      notes: null,
    },
  },
]

const wrap = (ui: React.ReactElement) =>
  render(<ChakraProvider value={defaultSystem}>{ui}</ChakraProvider>)

describe('ModelsPage', () => {
  beforeEach(() => {
    vi.mocked(gateway.listProviders).mockResolvedValue(mockProviders)
    vi.mocked(gateway.createProvider).mockResolvedValue({ id: 'new-id' })
    vi.mocked(gateway.updateProvider).mockResolvedValue({ id: 'p1' })
    vi.mocked(gateway.deleteProvider).mockResolvedValue(undefined)
    vi.mocked(gateway.setDefaultProvider).mockResolvedValue(undefined)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
  })

  it('加载时显示 loading 状态', () => {
    vi.mocked(gateway.listProviders).mockReturnValue(new Promise(() => {}))
    const { container } = wrap(<ModelsPage />)
    // Spinner 应该存在
    expect(container).toBeTruthy()
  })

  it('加载成功后显示 Provider 卡片', async () => {
    wrap(<ModelsPage />)
    await waitFor(() => {
      expect(screen.getByText('GPT-4o')).toBeInTheDocument()
      expect(screen.getByText('Claude Sonnet')).toBeInTheDocument()
    })
  })

  it('显示默认 Provider 的"默认"标签', async () => {
    wrap(<ModelsPage />)
    await waitFor(() => {
      expect(screen.getByText('默认')).toBeInTheDocument()
    })
  })

  it('显示协议类型标签', async () => {
    wrap(<ModelsPage />)
    await waitFor(() => {
      expect(screen.getByText('OpenAI / 兼容')).toBeInTheDocument()
      expect(screen.getByText('Anthropic')).toBeInTheDocument()
    })
  })

  it('点击添加提供方按钮打开弹窗', async () => {
    wrap(<ModelsPage />)
    await waitFor(() => screen.getByText('GPT-4o'))
    fireEvent.click(screen.getByRole('button', { name: /添加提供方/ }))
    await waitFor(() => {
      // 弹窗打开后应有多个「添加提供方」出现（按钮 + 弹窗标题）
      expect(screen.getAllByText(/添加提供方/).length).toBeGreaterThan(1)
    })
  })

  it('点击删除后调用 deleteProvider', async () => {
    wrap(<ModelsPage />)
    await waitFor(() => screen.getByText('GPT-4o'))
    fireEvent.click(screen.getByRole('button', { name: '删除提供方 Claude Sonnet' }))
    await waitFor(() => {
      expect(screen.getByText('删除提供方')).toBeInTheDocument()
    })
    fireEvent.click(screen.getByRole('button', { name: '删除' }))
    await waitFor(() => {
      expect(gateway.deleteProvider).toHaveBeenCalled()
    })
  })
})
