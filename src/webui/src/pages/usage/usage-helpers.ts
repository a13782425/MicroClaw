export const PIE_COLORS = [
  'blue.solid', 'green.solid', 'orange.solid', 'purple.solid',
  'teal.solid', 'pink.solid', 'cyan.solid', 'red.solid',
] as const

export const COST_COLORS = [
  'blue.solid', 'green.solid', 'orange.solid', 'teal.solid',
  'pink.solid', 'cyan.solid', 'red.solid', 'yellow.solid',
] as const

export const TOKEN_SERIES = [
  { label: '输入 Token', dataKey: 'inputTokens', color: 'blue.solid', gradientId: 'token-input' },
  { label: '输出 Token', dataKey: 'outputTokens', color: 'green.solid', gradientId: 'token-output' },
  { label: '总 Token', dataKey: 'totalTokens', color: 'orange.solid', gradientId: 'token-total' },
] as const

export function fmtTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return String(n)
}

function toISODate(d: Date): string {
  const year = d.getFullYear()
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function createDefaultDateRange() {
  const now = new Date()
  const start = new Date(now)
  start.setDate(now.getDate() - 29)

  return {
    startDate: toISODate(start),
    endDate: toISODate(now),
  }
}
