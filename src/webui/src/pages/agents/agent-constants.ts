import { createListCollection } from '@chakra-ui/react'

export const ROUTING_STRATEGY_OPTIONS = [
  { value: 'Default', label: '默认（使用默认 Provider）' },
  { value: 'QualityFirst', label: '质量优先' },
  { value: 'CostFirst', label: '成本优先' },
  { value: 'LatencyFirst', label: '延迟优先' },
]

export const routingStrategyCollection = createListCollection({ items: ROUTING_STRATEGY_OPTIONS })

export function routingStrategyLabel(strategy: string): string {
  return ROUTING_STRATEGY_OPTIONS.find((option) => option.value === strategy)?.label ?? strategy
}

export const DIMENSION_COLORS = {
  alertness: '#3182ce',
  mood: '#38a169',
  curiosity: '#d69e2e',
  confidence: '#e53e3e',
}

export const DIMENSION_LABELS: Record<string, string> = {
  alertness: '警觉度',
  mood: '心情',
  curiosity: '好奇心',
  confidence: '信心',
}

export type ChartPoint = {
  time: string
  alertness: number
  mood: number
  curiosity: number
  confidence: number
}

export function toISODateLocal(ms: number): string {
  const date = new Date(ms)
  return date.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}
