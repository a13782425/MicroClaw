import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, Button, HStack, VStack, SimpleGrid,
  Badge, Spinner, Table,
} from '@chakra-ui/react'
import { RefreshCw, Activity, Clock, AlertCircle, CheckCircle } from 'lucide-react'
import {
  getDevMetrics,
  getDevContextProviders,
  getDevMiddlewareLimits,
  type DevMetricsSnapshot,
  type ContextProviderInfoDto,
  type MiddlewareLimitsDto,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'

function StatCard({
  label,
  value,
  sub,
  color,
}: {
  label: string
  value: string | number
  sub?: string
  color?: string
}) {
  return (
    <Box p={4} borderWidth="1px" borderColor="var(--mc-border)" borderRadius="lg" bg="var(--mc-card)">
      <Text fontSize="xs" color="var(--mc-text-muted)" mb={1}>{label}</Text>
      <Text fontSize="2xl" fontWeight="bold" color={color}>{value}</Text>
      {sub && <Text fontSize="xs" color="var(--mc-text-muted)" mt={1}>{sub}</Text>}
    </Box>
  )
}

function formatMs(ms: number): string {
  if (ms >= 1000) return `${(ms / 1000).toFixed(2)}s`
  return `${ms}ms`
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false })
}

function uptime(startedAt: string): string {
  const secs = Math.floor((Date.now() - new Date(startedAt).getTime()) / 1000)
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  return `${h}h ${m}m ${s}s`
}

export default function DevPage() {
  const [metrics, setMetrics] = useState<DevMetricsSnapshot | null>(null)
  const [providers, setProviders] = useState<ContextProviderInfoDto[]>([])
  const [limits, setLimits] = useState<MiddlewareLimitsDto | null>(null)
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [m, p, l] = await Promise.all([
        getDevMetrics(),
        getDevContextProviders(),
        getDevMiddlewareLimits(),
      ])
      setMetrics(m)
      setProviders(Array.isArray(p) ? p : [])
      setLimits(l)
    } catch {
      toaster.error({ title: '无法加载 DevUI 数据', description: '请确保在 Development 环境下运行' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const toolEntries = metrics?.toolStats
    ? Object.entries(metrics.toolStats).sort((a, b) => b[1].callCount - a[1].callCount)
    : []

  const successRate =
    metrics && metrics.totalAgentRuns > 0
      ? (((metrics.totalAgentRuns - metrics.failedAgentRuns) / metrics.totalAgentRuns) * 100).toFixed(1)
      : '—'

  return (
    <Box p={6} maxW="1400px" mx="auto" color="var(--mc-text)">
      {/* 页头 */}
      <HStack mb={6} justify="space-between">
        <VStack align="start" gap={0}>
          <HStack>
            <Activity size={20} />
            <Text fontSize="xl" fontWeight="bold">DevUI — 调试控制台</Text>
            <Badge colorPalette="orange" variant="subtle" size="sm">Development Only</Badge>
          </HStack>
          <Text fontSize="sm" color="var(--mc-text-muted)">
            实时展示 Agent 执行指标、工具耗时与中间件配置
          </Text>
        </VStack>
        <Button
          size="sm"
          variant="outline"
          data-mc-refresh="true"
          onClick={load}
          loading={loading}
          color="var(--mc-text)"
          borderColor="var(--mc-border)"
          _hover={{ bg: 'var(--mc-card-hover)' }}
        >
          <RefreshCw size={14} />
          刷新
        </Button>
      </HStack>

      {loading && !metrics && (
        <Box textAlign="center" py={12}>
          <Spinner />
          <Text mt={2} color="var(--mc-text-muted)">加载中...</Text>
        </Box>
      )}

      {metrics && (
        <>
          {/* 总览统计卡片 */}
          <SimpleGrid columns={{ base: 2, md: 4 }} gap={4} mb={6}>
            <StatCard
              label="服务已运行"
              value={uptime(metrics.startedAt)}
              sub={`启动于 ${formatDate(metrics.startedAt)}`}
            />
            <StatCard
              label="Agent 总运行次数"
              value={metrics.totalAgentRuns}
              color={metrics.totalAgentRuns > 0 ? 'var(--mc-primary)' : undefined}
            />
            <StatCard
              label="失败次数"
              value={metrics.failedAgentRuns}
              color={metrics.failedAgentRuns > 0 ? 'var(--mc-danger)' : 'var(--mc-success)'}
            />
            <StatCard
              label="成功率"
              value={successRate === '—' ? '—' : `${successRate}%`}
              color={parseFloat(successRate) >= 90 ? 'var(--mc-success)' : 'var(--mc-warning)'}
            />
          </SimpleGrid>

          {/* 工具耗时统计表 */}
          <Box mb={6} borderWidth="1px" borderColor="var(--mc-border)" borderRadius="lg" overflow="hidden">
            <Box px={4} py={3} bg="var(--mc-input)" borderBottomWidth="1px" borderColor="var(--mc-border)">
              <HStack>
                <Clock size={16} />
                <Text fontWeight="semibold">工具执行耗时统计</Text>
                <Badge variant="subtle" size="sm">{toolEntries.length} 个工具</Badge>
              </HStack>
            </Box>
            {toolEntries.length === 0 ? (
              <Box px={4} py={8} textAlign="center" color="var(--mc-text-muted)" fontSize="sm">
                暂无工具调用记录
              </Box>
            ) : (
              <Table.Root variant="line" size="sm">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeader>工具名</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">调用次数</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">失败次数</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">平均耗时</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">最大耗时</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">总耗时</Table.ColumnHeader>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {toolEntries.map(([name, stat]) => (
                    <Table.Row key={name}>
                      <Table.Cell>
                        <Text fontFamily="mono" fontSize="xs">{name}</Text>
                      </Table.Cell>
                      <Table.Cell textAlign="right">{stat.callCount}</Table.Cell>
                      <Table.Cell textAlign="right">
                        <Text color={stat.errorCount > 0 ? 'var(--mc-danger)' : 'var(--mc-text-muted)'}>
                          {stat.errorCount}
                        </Text>
                      </Table.Cell>
                      <Table.Cell textAlign="right">{formatMs(Math.round(stat.averageElapsedMs))}</Table.Cell>
                      <Table.Cell textAlign="right">{formatMs(stat.maxElapsedMs)}</Table.Cell>
                      <Table.Cell textAlign="right">{formatMs(stat.totalElapsedMs)}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            )}
          </Box>

          {/* 最近运行记录 */}
          <Box mb={6} borderWidth="1px" borderColor="var(--mc-border)" borderRadius="lg" overflow="hidden">
            <Box px={4} py={3} bg="var(--mc-input)" borderBottomWidth="1px" borderColor="var(--mc-border)">
              <HStack>
                <Activity size={16} />
                <Text fontWeight="semibold">最近 Agent 运行记录</Text>
                <Badge variant="subtle" size="sm">最多显示 100 条</Badge>
              </HStack>
            </Box>
            {(metrics.recentRuns ?? []).length === 0 ? (
              <Box px={4} py={8} textAlign="center" color="var(--mc-text-muted)" fontSize="sm">
                暂无运行记录
              </Box>
            ) : (
              <Table.Root variant="line" size="sm">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeader>状态</Table.ColumnHeader>
                    <Table.ColumnHeader>Agent ID</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">耗时</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">时间</Table.ColumnHeader>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {[...(metrics.recentRuns ?? [])].reverse().map((run, idx) => (
                    <Table.Row key={idx}>
                      <Table.Cell>
                        {run.success
                          ? <HStack gap={1}><CheckCircle size={14} color="var(--mc-success)" /><Text fontSize="xs" color="var(--mc-success)">成功</Text></HStack>
                          : <HStack gap={1}><AlertCircle size={14} color="var(--mc-danger)" /><Text fontSize="xs" color="var(--mc-danger)">失败</Text></HStack>}
                      </Table.Cell>
                      <Table.Cell>
                        <Text fontFamily="mono" fontSize="xs">{run.agentId}</Text>
                      </Table.Cell>
                      <Table.Cell textAlign="right">{formatMs(run.durationMs)}</Table.Cell>
                      <Table.Cell textAlign="right">
                        <Text fontSize="xs" color="var(--mc-text-muted)">{formatDate(run.executedAt)}</Text>
                      </Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            )}
          </Box>
        </>
      )}

      {/* Context Providers + 中间件限制（横排两列） */}
      <SimpleGrid columns={{ base: 1, md: 2 }} gap={4}>
        {/* Context Providers */}
        <Box borderWidth="1px" borderColor="var(--mc-border)" borderRadius="lg" overflow="hidden">
          <Box px={4} py={3} bg="var(--mc-input)" borderBottomWidth="1px" borderColor="var(--mc-border)">
            <Text fontWeight="semibold">Context Providers</Text>
          </Box>
          {providers.length === 0 ? (
            <Box px={4} py={6} textAlign="center" color="var(--mc-text-muted)" fontSize="sm">
              {loading ? <Spinner size="sm" /> : '无数据'}
            </Box>
          ) : (
            <Table.Root variant="line" size="sm">
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeader>名称</Table.ColumnHeader>
                  <Table.ColumnHeader textAlign="right">Order</Table.ColumnHeader>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {providers.map((p) => (
                  <Table.Row key={p.order}>
                    <Table.Cell>
                      <Text fontFamily="mono" fontSize="xs">{p.name}</Text>
                    </Table.Cell>
                    <Table.Cell textAlign="right">
                      <Badge variant="outline" size="sm">{p.order}</Badge>
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          )}
        </Box>

        {/* 中间件限制 */}
        <Box borderWidth="1px" borderColor="var(--mc-border)" borderRadius="lg" overflow="hidden">
          <Box px={4} py={3} bg="var(--mc-input)" borderBottomWidth="1px" borderColor="var(--mc-border)">
            <Text fontWeight="semibold">中间件限制参数</Text>
          </Box>
          {!limits?.iterations ? (
            <Box px={4} py={6} textAlign="center" color="var(--mc-text-muted)" fontSize="sm">
              {loading ? <Spinner size="sm" /> : '无数据'}
            </Box>
          ) : (
            <Table.Root variant="line" size="sm">
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeader>参数</Table.ColumnHeader>
                  <Table.ColumnHeader textAlign="right">值</Table.ColumnHeader>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                <Table.Row>
                  <Table.Cell>最大工具调用轮次（上限）</Table.Cell>
                  <Table.Cell textAlign="right">
                    <Badge variant="outline" size="sm">{limits.iterations.max}</Badge>
                  </Table.Cell>
                </Table.Row>
                <Table.Row>
                  <Table.Cell>最小工具调用轮次</Table.Cell>
                  <Table.Cell textAlign="right">
                    <Badge variant="outline" size="sm">{limits.iterations.min}</Badge>
                  </Table.Cell>
                </Table.Row>
                <Table.Row>
                  <Table.Cell>子代理最大递归深度</Table.Cell>
                  <Table.Cell textAlign="right">
                    <Badge variant="outline" size="sm">{limits.maxDepth?.default ?? '—'}</Badge>
                  </Table.Cell>
                </Table.Row>
              </Table.Body>
            </Table.Root>
          )}
        </Box>
      </SimpleGrid>
    </Box>
  )
}
