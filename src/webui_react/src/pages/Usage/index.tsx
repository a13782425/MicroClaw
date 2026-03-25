import { useState, useEffect, useMemo } from 'react'
import {
  Box, Text, Button, HStack, SimpleGrid, Card, Spinner, Table,
} from '@chakra-ui/react'
import { Chart, useChart } from '@chakra-ui/charts'
import { RefreshCw, TrendingUp, TrendingDown, Coins, DollarSign } from 'lucide-react'
import {
  AreaChart, Area, BarChart, Bar, PieChart, Pie, Sector,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend,
  type PieSectorShapeProps,
} from 'recharts'
import { fetchUsageStats, type UsageQueryResult } from '@/api/gateway'
import { DateInput } from '@/components/ui/date-input'
import { toaster } from '@/components/ui/toaster'

const PIE_COLORS = [
  'blue.solid', 'green.solid', 'orange.solid', 'purple.solid',
  'teal.solid', 'pink.solid', 'cyan.solid', 'red.solid',
] as const

function fmtTokens(n: number): string {
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

function createDefaultDateRange() {
  const now = new Date()
  const start = new Date(now)
  start.setDate(now.getDate() - 29)

  return {
    startDate: toISODate(start),
    endDate: toISODate(now),
  }
}

function DateRangeFilter({
  startDate,
  endDate,
  loading,
  onStartDateChange,
  onEndDateChange,
  onSearch,
}: {
  startDate: string
  endDate: string
  loading: boolean
  onStartDateChange: (value: string) => void
  onEndDateChange: (value: string) => void
  onSearch: () => void
}) {
  return (
    <HStack>
      <DateInput
        ariaLabel="开始日期"
        value={startDate}
        onChange={onStartDateChange}
        placeholder="开始日期"
      />
      <Text fontSize="sm" color="gray.400">—</Text>
      <DateInput
        ariaLabel="结束日期"
        value={endDate}
        onChange={onEndDateChange}
        placeholder="结束日期"
      />
      <Button size="sm" colorPalette="blue" loading={loading} onClick={onSearch}>
        <RefreshCw size={14} />查询
      </Button>
    </HStack>
  )
}

// ─── 统计卡片 ──────────────────────────────────────────────────────────────────

function StatCard({ label, value, icon: Icon, color }: { label: string; value: string; icon: React.ElementType; color: string }) {
  return (
    <Card.Root>
      <Card.Body>
        <HStack justify="space-between">
          <Box>
            <Text fontSize="xs" color="gray.500" mb="1">{label}</Text>
            <Text fontSize="xl" fontWeight="bold">{value}</Text>
          </Box>
          <Box color={color} opacity={0.8}><Icon size={28} /></Box>
        </HStack>
      </Card.Body>
    </Card.Root>
  )
}

// ─── Provider 饼图 ─────────────────────────────────────────────────────────────

function ProviderPieChart({ data }: { data: UsageQueryResult['byProvider'] }) {
  const pieData = useMemo(() =>
    data.map((p, i) => ({
      name: p.providerName,
      value: p.inputTokens + p.outputTokens,
      color: PIE_COLORS[i % PIE_COLORS.length],
    })),
    [data],
  )

  const chart = useChart({ data: pieData })

  return (
    <Chart.Root boxSize="240px" mx="auto" chart={chart}>
      <PieChart responsive>
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip hideLabel />} />
        <Legend content={<Chart.Legend />} />
        <Pie
          isAnimationActive={false}
          data={chart.data}
          dataKey={chart.key('value')}
          nameKey="name"
          outerRadius={80}
          labelLine={false}
          label={({ index }) => {
            const item = chart.data[index ?? -1]
            if (!item) return ''
            const total = chart.getTotal('value')
            return total > 0 ? `${((item.value / total) * 100).toFixed(1)}%` : ''
          }}
          shape={(props: PieSectorShapeProps) => (
            <Sector {...props} fill={chart.color((props.payload as { color: string })?.color)} />
          )}
        />
      </PieChart>
    </Chart.Root>
  )
}

// ─── Source 饼图 ───────────────────────────────────────────────────────────────

function SourcePieChart({ data }: { data: UsageQueryResult['bySource'] }) {
  const pieData = useMemo(() =>
    data.map((s, i) => ({
      name: s.source || '未知',
      value: s.inputTokens + s.outputTokens,
      color: PIE_COLORS[(i + 2) % PIE_COLORS.length],
    })),
    [data],
  )

  const chart = useChart({ data: pieData })

  return (
    <Chart.Root boxSize="240px" mx="auto" chart={chart}>
      <PieChart responsive>
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip hideLabel />} />
        <Legend content={<Chart.Legend />} />
        <Pie
          isAnimationActive={false}
          data={chart.data}
          dataKey={chart.key('value')}
          nameKey="name"
          outerRadius={80}
          labelLine={false}
          label={({ index }) => {
            const item = chart.data[index ?? -1]
            if (!item) return ''
            const total = chart.getTotal('value')
            return total > 0 ? `${((item.value / total) * 100).toFixed(1)}%` : ''
          }}
          shape={(props: PieSectorShapeProps) => (
            <Sector {...props} fill={chart.color((props.payload as { color: string })?.color)} />
          )}
        />
      </PieChart>
    </Chart.Root>
  )
}

// ─── Token 日趋势 AreaChart ──────────────────────────────────────────────────

function TokenTrendChart({ data }: { data: UsageQueryResult['daily'] }) {
  const chart = useChart({
    data,
    series: [
      { name: 'inputTokens', color: 'blue.solid' },
      { name: 'outputTokens', color: 'green.solid' },
    ],
  })

  return (
    <Chart.Root maxH="sm" chart={chart}>
      <AreaChart data={chart.data} responsive>
        <CartesianGrid stroke={chart.color('border.muted')} vertical={false} strokeDasharray="3 3" />
        <XAxis
          dataKey={chart.key('date')}
          axisLine={false}
          tickLine={false}
          tick={{ fontSize: 11 }}
        />
        <YAxis
          axisLine={false}
          tickLine={false}
          tickFormatter={(v) => fmtTokens(v)}
          tick={{ fontSize: 11 }}
        />
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip />} />
        <Legend content={<Chart.Legend />} />

        {chart.series.map((item) => (
          <defs key={`${item.name}-def`}>
            <Chart.Gradient
              id={`${item.name}-gradient`}
              stops={[
                { offset: '0%', color: item.color, opacity: 0.4 },
                { offset: '100%', color: item.color, opacity: 0.05 },
              ]}
            />
          </defs>
        ))}

        {chart.series.map((item) => (
          <Area
            key={item.name}
            type="monotone"
            isAnimationActive={false}
            dataKey={chart.key(item.name)}
            fill={`url(#${item.name}-gradient)`}
            stroke={chart.color(item.color)}
            strokeWidth={2}
            name={item.name === 'inputTokens' ? '输入 Token' : '输出 Token'}
          />
        ))}
      </AreaChart>
    </Chart.Root>
  )
}

// ─── 费用趋势 AreaChart ─────────────────────────────────────────────────────

function CostTrendChart({ data }: { data: UsageQueryResult['daily'] }) {
  const chart = useChart({
    data,
    series: [{ name: 'estimatedCostUsd', color: 'purple.solid' }],
  })

  return (
    <Chart.Root maxH="xs" chart={chart}>
      <AreaChart data={chart.data} responsive>
        <CartesianGrid stroke={chart.color('border.muted')} vertical={false} strokeDasharray="3 3" />
        <XAxis
          dataKey={chart.key('date')}
          axisLine={false}
          tickLine={false}
          tick={{ fontSize: 11 }}
        />
        <YAxis
          axisLine={false}
          tickLine={false}
          tickFormatter={(v) => `$${Number(v).toFixed(2)}`}
          tick={{ fontSize: 11 }}
        />
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip />} />

        <defs>
          <Chart.Gradient
            id="cost-gradient"
            stops={[
              { offset: '0%', color: 'purple.solid', opacity: 0.4 },
              { offset: '100%', color: 'purple.solid', opacity: 0.05 },
            ]}
          />
        </defs>

        <Area
          type="monotone"
          isAnimationActive={false}
          dataKey={chart.key('estimatedCostUsd')}
          fill="url(#cost-gradient)"
          stroke={chart.color('purple.solid')}
          strokeWidth={2}
          name="预估费用（USD）"
        />
      </AreaChart>
    </Chart.Root>
  )
}

function ProviderBarChart({ data }: { data: UsageQueryResult['byProvider'] }) {
  const chart = useChart({
    data,
    series: [
      { name: 'inputTokens', color: 'blue.solid' },
      { name: 'outputTokens', color: 'green.solid' },
    ],
  })

  return (
    <Chart.Root maxH="xs" chart={chart}>
      <BarChart data={chart.data} responsive>
        <CartesianGrid strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="providerName" tick={{ fontSize: 11 }} />
        <YAxis tickFormatter={(v) => fmtTokens(v)} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} />
        <Legend />
        <Bar dataKey="inputTokens" fill="#3b82f6" name="输入 Token" />
        <Bar dataKey="outputTokens" fill="#22c55e" name="输出 Token" />
      </BarChart>
    </Chart.Root>
  )
}

function SourceBarChart({ data }: { data: UsageQueryResult['bySource'] }) {
  const chart = useChart({
    data,
    series: [
      { name: 'inputTokens', color: 'blue.solid' },
      { name: 'outputTokens', color: 'green.solid' },
    ],
  })

  return (
    <Chart.Root maxH="xs" chart={chart}>
      <BarChart data={chart.data} responsive>
        <CartesianGrid strokeDasharray="3 3" vertical={false} />
        <XAxis dataKey="source" tick={{ fontSize: 11 }} />
        <YAxis tickFormatter={(v) => fmtTokens(v)} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} />
        <Legend />
        <Bar dataKey="inputTokens" fill="#3b82f6" name="输入 Token" />
        <Bar dataKey="outputTokens" fill="#22c55e" name="输出 Token" />
      </BarChart>
    </Chart.Root>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function UsagePage() {
  const defaultRange = useMemo(() => createDefaultDateRange(), [])
  const [startDate, setStartDate] = useState(defaultRange.startDate)
  const [endDate, setEndDate] = useState(defaultRange.endDate)
  const [data, setData] = useState<UsageQueryResult | null>(null)
  const [loading, setLoading] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const res = await fetchUsageStats(startDate, endDate)
      setData(res)
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
          {/* 统计卡片 */}
          <SimpleGrid columns={{ base: 2, md: 4 }} gap="4" mb="6">
            <StatCard label="输入 Token" value={fmtTokens(summary?.totalInputTokens ?? 0)} icon={TrendingDown} color="blue.500" />
            <StatCard label="输出 Token" value={fmtTokens(summary?.totalOutputTokens ?? 0)} icon={TrendingUp} color="green.500" />
            <StatCard label="总 Token" value={fmtTokens((summary?.totalInputTokens ?? 0) + (summary?.totalOutputTokens ?? 0))} icon={Coins} color="orange.500" />
            <StatCard label="估算费用（USD）" value={`$${(summary?.totalCostUsd ?? 0).toFixed(4)}`} icon={DollarSign} color="purple.500" />
          </SimpleGrid>

          {/* AreaChart：Token 日趋势 费用趋势 */}
          {data.daily && data.daily.length > 0 && (
             <SimpleGrid columns={{ base: 1, md: 2 }} gap="4" mb="6">
              <Box mb="6" p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">Token 日趋势</Text>
                <TokenTrendChart data={data.daily} />
              </Box>
              {/* AreaChart：费用趋势 */}
              <Box mb="6" p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">费用趋势（USD）</Text>
                <CostTrendChart data={data.daily} />
              </Box>
            </SimpleGrid>
          )}

      
          {/* Provider：占比 + 绝对量 */}
          {data.byProvider.length > 0 && (
            <SimpleGrid columns={{ base: 1, md: 2 }} gap="4" mb="6">
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按 Provider 占比</Text>
                <ProviderPieChart data={data.byProvider} />
              </Box>
              <Box p="4" borderWidth="1px" rounded="md">
                <Text fontWeight="medium" mb="3" fontSize="sm">按 Provider 分组（绝对量）</Text>
                <ProviderBarChart data={data.byProvider} />
              </Box>
            </SimpleGrid>
          )}

          {/* Source：占比 + 绝对量 */}
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
            <Box mb="6">
              <Text fontWeight="medium" mb="3" fontSize="sm">按来源明细</Text>
              <Table.Root variant="outline">
                <Table.Header>
                  <Table.Row>
                    <Table.ColumnHeader>来源</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="end">输入 Token</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="end">输出 Token</Table.ColumnHeader>
                    <Table.ColumnHeader textAlign="end">合计</Table.ColumnHeader>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {data.bySource.map((row) => (
                    <Table.Row key={row.source}>
                      <Table.Cell fontSize="sm">{row.source || '—'}</Table.Cell>
                      <Table.Cell textAlign="end" fontSize="sm">{fmtTokens(row.inputTokens)}</Table.Cell>
                      <Table.Cell textAlign="end" fontSize="sm">{fmtTokens(row.outputTokens)}</Table.Cell>
                      <Table.Cell textAlign="end" fontSize="sm" fontWeight="medium">{fmtTokens(row.inputTokens + row.outputTokens)}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            </Box>
          )}

          

          {data.daily.length === 0 && data.byProvider.length === 0 && data.bySource.length === 0 && (
            <Box py="8" textAlign="center" color="gray.400">该时间段内无用量数据</Box>
          )}
        </>
      )}
    </Box>
  )
}

