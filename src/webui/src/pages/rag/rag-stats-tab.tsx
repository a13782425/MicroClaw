import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, HStack, Spinner, NativeSelect, Card, SimpleGrid, Progress, Button,
} from '@chakra-ui/react'
import { RefreshCw } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { getRagQueryStats, type RagQueryStats } from '@/api/gateway'
import { type ScopeFilter } from './rag-utils'

function StatCard({ label, value, sub }: { label: string; value: string | number; sub?: string }) {
  return (
    <Card.Root variant="outline">
      <Card.Body py="4" px="5">
        <Text fontSize="2xl" fontWeight="bold" lineHeight="1.2">{value}</Text>
        <Text fontSize="sm" fontWeight="semibold" mt="1">{label}</Text>
        {sub && <Text fontSize="xs" color="gray.400" mt="0.5">{sub}</Text>}
      </Card.Body>
    </Card.Root>
  )
}

export function RagStatsTab() {
  const [scopeFilter, setScopeFilter] = useState<ScopeFilter>('All')
  const [stats, setStats] = useState<RagQueryStats | null>(null)
  const [loading, setLoading] = useState(false)

  const fetchStats = useCallback(async () => {
    setLoading(true)
    try {
      const result = await getRagQueryStats(scopeFilter === 'All' ? undefined : scopeFilter)
      setStats(result)
    } catch {
      toaster.create({ type: 'error', title: '加载统计数据失败' })
    } finally {
      setLoading(false)
    }
  }, [scopeFilter])

  useEffect(() => { fetchStats() }, [fetchStats])

  const hitRatePct = stats ? Math.round(stats.hitRate * 100) : 0

  return (
    <Box>
      <HStack mb="5" gap="3" justify="space-between">
        <HStack gap="3">
          <Text fontSize="sm" fontWeight="medium" whiteSpace="nowrap">作用域：</Text>
          <NativeSelect.Root size="sm" maxW="160px">
            <NativeSelect.Field
              value={scopeFilter}
              onChange={(e) => setScopeFilter(e.target.value as ScopeFilter)}
            >
              <option value="All">全部</option>
              <option value="Global">全局库</option>
              <option value="Session">会话库</option>
            </NativeSelect.Field>
            <NativeSelect.Indicator />
          </NativeSelect.Root>
        </HStack>
        <Button size="sm" variant="outline" onClick={fetchStats} loading={loading}>
          <RefreshCw size={14} />
          刷新
        </Button>
      </HStack>

      {loading && !stats ? (
        <Box py="16" textAlign="center">
          <Spinner size="lg" color="blue.500" />
        </Box>
      ) : stats ? (
        <Box>
          <SimpleGrid columns={{ base: 2, md: 4 }} gap="4" mb="6">
            <StatCard label="总查询次数" value={stats.totalQueries} sub="历史累计" />
            <StatCard label="近 24h 查询" value={stats.last24hQueries} sub="最近活跃度" />
            <StatCard label="平均延迟" value={`${stats.avgElapsedMs} ms`} sub="混合检索耗时" />
            <StatCard label="平均召回数" value={stats.avgRecallCount} sub="每次返回结果" />
          </SimpleGrid>

          <Card.Root variant="outline" mb="4">
            <Card.Body py="5" px="6">
              <HStack justify="space-between" mb="3">
                <Box>
                  <Text fontWeight="semibold">命中率</Text>
                  <Text fontSize="xs" color="gray.500" mt="0.5">
                    召回结果 &gt; 0 的查询占比（{stats.hitQueries} / {stats.totalQueries}）
                  </Text>
                </Box>
                <Text fontSize="2xl" fontWeight="bold" color={hitRatePct >= 80 ? 'green.500' : hitRatePct >= 50 ? 'orange.500' : 'red.500'}>
                  {hitRatePct}%
                </Text>
              </HStack>
              <Progress.Root value={hitRatePct} size="lg" colorPalette={hitRatePct >= 80 ? 'green' : hitRatePct >= 50 ? 'orange' : 'red'}>
                <Progress.Track borderRadius="full">
                  <Progress.Range borderRadius="full" />
                </Progress.Track>
              </Progress.Root>
            </Card.Body>
          </Card.Root>

          {stats.totalQueries === 0 && (
            <Box p="4" bg="blue.50" borderRadius="md" border="1px solid" borderColor="blue.200">
              <Text fontSize="sm" color="blue.700">
                暂无检索记录。Agent 进行 RAG 检索后，统计数据将在此处显示。
              </Text>
            </Box>
          )}
        </Box>
      ) : null}
    </Box>
  )
}
