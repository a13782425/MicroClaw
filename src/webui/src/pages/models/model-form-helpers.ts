import { createListCollection } from '@chakra-ui/react'
import type { ProviderCapabilities, ProviderProtocol } from '@/api/gateway'

export function protocolLabel(p: ProviderProtocol): string {
  return p === 'openai' ? 'OpenAI / 兼容' : 'Anthropic'
}

export const PROTOCOL_OPTIONS = [
  { value: 'openai', label: 'OpenAI / 兼容' },
  { value: 'anthropic', label: 'Anthropic (Claude)' },
]

export const protocolCollection = createListCollection({ items: PROTOCOL_OPTIONS })

export const EMBEDDING_PROTOCOL_OPTIONS = [
  { value: 'openai', label: 'OpenAI / 兼容' },
]

export const embeddingProtocolCollection = createListCollection({ items: EMBEDDING_PROTOCOL_OPTIONS })

export const LATENCY_TIER_OPTIONS = [
  { value: 'Low', label: '低延迟（本地/轻量云端）' },
  { value: 'Medium', label: '中等延迟（标准云端）' },
  { value: 'High', label: '高延迟（大上下文/推理型）' },
]

export const latencyTierCollection = createListCollection({ items: LATENCY_TIER_OPTIONS })

export function latencyTierLabel(tier: string): string {
  return LATENCY_TIER_OPTIONS.find((option) => option.value === tier)?.label.split('（')[0] ?? tier
}

export function latencyTierColor(tier: string): string {
  if (tier === 'Low') return 'green'
  if (tier === 'High') return 'orange'
  return 'blue'
}

export function latencyTierBg(tier: string): string {
  if (tier === 'Low') return 'var(--mc-success-soft)'
  if (tier === 'High') return 'var(--mc-warning-soft)'
  return 'var(--mc-primary-soft)'
}

export function latencyTierFg(tier: string): string {
  if (tier === 'Low') return 'var(--mc-success)'
  if (tier === 'High') return 'var(--mc-warning)'
  return 'var(--mc-primary)'
}

export const INPUT_MODALITIES = [
  { key: 'inputImage', label: '图片' },
  { key: 'inputAudio', label: '音频' },
  { key: 'inputVideo', label: '视频' },
  { key: 'inputFile', label: '文件' },
] as const

export const OUTPUT_MODALITIES = [
  { key: 'outputImage', label: '图片' },
  { key: 'outputAudio', label: '音频' },
  { key: 'outputVideo', label: '视频' },
] as const

export function defaultChatForm() {
  return {
    displayName: '',
    protocol: 'openai' as ProviderProtocol,
    baseUrl: '',
    apiKey: '',
    modelName: '',
    maxOutputTokens: 8192,
    isEnabled: true,
    inputImage: false,
    inputAudio: false,
    inputVideo: false,
    inputFile: false,
    outputImage: false,
    outputAudio: false,
    outputVideo: false,
    supportsFunctionCalling: true,
    supportsResponsesApi: false,
    inputPricePerMToken: '',
    outputPricePerMToken: '',
    cacheInputPricePerMToken: '',
    cacheOutputPricePerMToken: '',
    notes: '',
    qualityScore: 50,
    latencyTier: 'Medium',
  }
}

export function defaultEmbeddingForm() {
  return {
    displayName: '',
    protocol: 'openai' as ProviderProtocol,
    baseUrl: '',
    apiKey: '',
    modelName: '',
    isEnabled: true,
    maxInputTokens: '',
    outputDimensions: '',
    inputPricePerMToken: '',
    notes: '',
  }
}

export type ChatFormState = ReturnType<typeof defaultChatForm>
export type EmbeddingFormState = ReturnType<typeof defaultEmbeddingForm>

export function hasNonDefaultCapabilities(caps: ProviderCapabilities | undefined | null): boolean {
  if (!caps) return false
  return caps.inputImage || caps.inputAudio || caps.inputVideo || caps.inputFile
    || caps.outputImage || caps.outputAudio || caps.outputVideo
    || caps.supportsFunctionCalling || caps.supportsResponsesApi
}

export function hasNonDefaultPricing(caps: ProviderCapabilities | undefined | null): boolean {
  if (!caps) return false
  return !!(caps.inputPricePerMToken || caps.outputPricePerMToken
    || caps.cacheInputPricePerMToken || caps.cacheOutputPricePerMToken
    || caps.notes)
}

export function buildChatCapabilities(form: ChatFormState): Partial<ProviderCapabilities> {
  return {
    inputImage: form.inputImage,
    inputAudio: form.inputAudio,
    inputVideo: form.inputVideo,
    inputFile: form.inputFile,
    outputImage: form.outputImage,
    outputAudio: form.outputAudio,
    outputVideo: form.outputVideo,
    supportsFunctionCalling: form.supportsFunctionCalling,
    supportsResponsesApi: form.supportsResponsesApi,
    inputPricePerMToken: parseFloat(form.inputPricePerMToken) || null,
    outputPricePerMToken: parseFloat(form.outputPricePerMToken) || null,
    cacheInputPricePerMToken: parseFloat(form.cacheInputPricePerMToken) || null,
    cacheOutputPricePerMToken: parseFloat(form.cacheOutputPricePerMToken) || null,
    notes: form.notes || null,
    qualityScore: form.qualityScore,
    latencyTier: form.latencyTier,
  }
}

export function buildEmbeddingCapabilities(form: EmbeddingFormState): Partial<ProviderCapabilities> {
  return {
    inputPricePerMToken: parseFloat(form.inputPricePerMToken) || null,
    maxInputTokens: parseInt(form.maxInputTokens) || null,
    outputDimensions: parseInt(form.outputDimensions) || null,
    notes: form.notes || null,
  }
}
