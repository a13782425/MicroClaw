import { useState, useEffect, useMemo } from 'react'
import {
  Box, Text, HStack, SimpleGrid, Spinner, Table,
} from '@chakra-ui/react'
import { TrendingUp, TrendingDown, Coins, DollarSign } from 'lucide-react'
import { fetchUsageStats, listAgents, type UsageQueryResult, type AgentConfig } from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { DateRangeFilter } from './date-range-filter'
import { UsageStatCard } from './usage-stat-card'
import {
  AgentUsageTable,
  CostTrendChart,
  ProviderBarChart,
  ProviderPieChart,
  SourceBarChart,
  SourcePieChart,
  TokenTrendChart,
} from './usage-charts'
import { createDefaultDateRange, fmtTokens } from './usage-helpers'

export default function UsagePage() {
  const defaultRange = useMemo(() => createDefaultDateRange(), [])
  const [startDate, setStartDate] = useState(defaultRange.startDate)
  const [endDate, setEndDate] = useState(defaultRange.endDate)
  const [data, setData] = useState<UsageQueryResult | null>(null)
  const [agents, setAgents] = useState<AgentConfig[]>([])
  const [loading, setLoading] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const usageResult = await fetchUsageStats(startDate, endDate)
      setData(usageResult)
      try {
        const agentList = await listAgents()
        setAgents(agentList)
      } catch {
        setAgents([])
      }
    } catch {
      toaster.create({ type: 'error', title: '加载用量统计失败' })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const summary = data?.summary

  return (
    <Box p="6">
      <HStack mb="5" justify="space-between" flexWrap="wrap" gap="3">
        <Text fontWeight="semibold" fontSize="lg">用量统计</Text>
        <DateRangeFilter
          startDate={startDate}
          endDate={endDate}
          loading={loading}
          onStartDateChange={setStartDate}
          onEndDateChange={setEndDate}
          onSearch={load}
        />
      </HStack>

      {loading && <Box py="8" textAlign="center"><Spinner /></Box>}

      {!loading && data && (
        <>
          <SimpleGrid columns={{ base: 2, md: 4 }} gap="4" mb="6">
            <UsageStatCard label="输入 Token" value={fmtTokens(summary?.totalInputTokens ?? 0)} icon={TrendingDown} color="blue.500" />
            <UsageStatCard label="输出 Token" value={fmtTokens(summary?.totalOutputTokens ?? 0)} icon={TrendingUp} color="green.500" />
            <UsageStatCard label="总 Token" value={fmtTokens((summary?.totalInputTokens ?? 0) + (summary?.totalOutputTokens ?? 0))} icon={Coins} color="orange.500" />
            <UsageStatCard label="估算费用（USD）" value={`$${(summary?.totalCostUsd ?? 0).toFixed(4)}`} icon={DollarSign} color="purple.500" />
          </SimpleGrid>

          {data.daily && data.daily.length > 0 && (
            <SimpleGrid columns={{ base: 1, md: 2 }} gap="4" mb="6">
              <Box mb="6" p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">Token 日趋势</Text>
                <TokenTrendChart data={data.daily} />
              </Box>
              <Box mb="6" p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">费用趋势（USD）</Text>
                <CostTrendChart daily={data.daily} dailyByProvider={data.dailyByProvider ?? []} />
              </Box>
            </SimpleGrid>
          )}

          {data.byProvider.length > 0 && (
            <SimpleGrid columns={{ base: 1, md: 2 }} gap="4" mb="6">
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按 模型 占比</Text>
                <ProviderPieChart data={data.byProvider} />
              </Box>
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按 模型 分组（绝对量）</Text>
                <ProviderBarChart data={data.byProvider} />
              </Box>
            </SimpleGrid>
          )}

          {data.bySource.length > 0 && (
            <SimpleGrid columns={{ base: 1, md: 2 }} gap="4" mb="6">
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按来源占比</Text>
                <SourcePieChart data={data.bySource} />
              </Box>
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按 Source 分组（绝对量）</Text>
                <SourceBarChart data={data.bySource} />
              </Box>
            </SimpleGrid>
          )}

          {data.bySource.length > 0 && (
            <Box p="4" borderWidth="1px" rounded="md" mb="6">
              <Text fontWeight="medium" mb="3" fontSize="sm">按来源明细</Text>
              <Table.Root size="sm">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeader>来源</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">输入 Token</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="right">输出 Token</Table.ColumnHeader>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {data.bySource.map((source) => (
                    <Table.Row key={source.source || 'unknown'}>
                      <Table.Cell>{source.source || '未知'}</Table.Cell>
                      <Table.Cell textAlign="right">{fmtTokens(source.inputTokens)}</Table.Cell>
                      <Table.Cell textAlign="right">{fmtTokens(source.outputTokens)}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            </Box>
          )}

          <Box p="4" borderWidth="1px" rounded="md" mb="6">
            <Text fontWeight="medium" mb="3" fontSize="sm">按 Agent 分组（含月度预算进度）</Text>
            <AgentUsageTable data={data.byAgent ?? []} agents={agents} />
          </Box>

          {data.daily.length === 0 && data.byProvider.length === 0 && data.bySource.length === 0 && (
            <Box py="8" textAlign="center" color="gray.400">该时间段内无用量数据</Box>
          )}
        </>
      )}
    </Box>
  )
}
