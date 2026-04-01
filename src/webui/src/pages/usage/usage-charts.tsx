import { useMemo } from 'react'
import { Box, Text, HStack, Progress, Table } from '@chakra-ui/react'
import { Chart, useChart } from '@chakra-ui/charts'
import {
  AreaChart, Area, BarChart, Bar, PieChart, Pie, Sector,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend,
  type PieSectorShapeProps,
} from 'recharts'
import type { UsageQueryResult, DailyProviderUsage, AgentConfig } from '@/api/gateway'
import { COST_COLORS, PIE_COLORS, TOKEN_SERIES, fmtTokens } from './usage-helpers'

export function ProviderPieChart({ data }: { data: UsageQueryResult['byProvider'] }) {
  const pieData = useMemo(() =>
    data.map((provider, index) => ({
      name: provider.providerName,
      value: provider.inputTokens + provider.outputTokens,
      color: PIE_COLORS[index % PIE_COLORS.length],
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

export function SourcePieChart({ data }: { data: UsageQueryResult['bySource'] }) {
  const pieData = useMemo(() =>
    data.map((source, index) => ({
      name: source.source || '未知',
      value: source.inputTokens + source.outputTokens,
      color: PIE_COLORS[(index + 2) % PIE_COLORS.length],
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

export function TokenTrendChart({ data }: { data: UsageQueryResult['daily'] }) {
  const enriched = useMemo(
    () => data.map((day) => ({
      date: day.date,
      '输入 Token': day.inputTokens,
      '输出 Token': day.outputTokens,
      '总 Token': day.inputTokens + day.outputTokens,
    })),
    [data],
  )

  const chart = useChart({
    data: enriched,
    series: TOKEN_SERIES.map(({ label, color }) => ({ name: label, color })),
  })

  return (
    <Chart.Root maxH="sm" chart={chart}>
      <AreaChart data={chart.data} responsive>
        <CartesianGrid stroke={chart.color('border.muted')} vertical={false} strokeDasharray="3 3" />
        <XAxis dataKey={chart.key('date')} axisLine={false} tickLine={false} tick={{ fontSize: 11 }} />
        <YAxis axisLine={false} tickLine={false} tickFormatter={(value) => fmtTokens(value)} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip />} />
        <Legend content={<Chart.Legend />} />

        {TOKEN_SERIES.map((item) => (
          <defs key={item.gradientId}>
            <Chart.Gradient
              id={item.gradientId}
              stops={[
                { offset: '0%', color: item.color, opacity: 0.4 },
                { offset: '100%', color: item.color, opacity: 0.05 },
              ]}
            />
          </defs>
        ))}

        {TOKEN_SERIES.map((item) => (
          <Area
            key={item.label}
            type="monotone"
            isAnimationActive={false}
            dataKey={chart.key(item.label)}
            fill={`url(#${item.gradientId})`}
            stroke={chart.color(item.color)}
            strokeWidth={2}
            name={item.label}
          />
        ))}
      </AreaChart>
    </Chart.Root>
  )
}

export function CostTrendChart({
  daily,
  dailyByProvider,
}: {
  daily: UsageQueryResult['daily']
  dailyByProvider: DailyProviderUsage[]
}) {
  const providerNames = useMemo(() => {
    const seen = new Set<string>()
    const names: string[] = []
    for (const row of dailyByProvider) {
      if (!seen.has(row.providerName)) {
        seen.add(row.providerName)
        names.push(row.providerName)
      }
    }
    return names
  }, [dailyByProvider])

  const totalLabel = '总费用（USD）'

  const pivoted = useMemo(() => {
    const index = new Map<string, number>()
    for (const row of dailyByProvider) {
      index.set(`${row.date}|${row.providerName}`, row.estimatedCostUsd)
    }
    return daily.map((day) => {
      const row: Record<string, unknown> = { date: day.date, [totalLabel]: day.estimatedCostUsd }
      for (const name of providerNames) {
        row[name] = index.get(`${day.date}|${name}`) ?? 0
      }
      return row
    })
  }, [daily, dailyByProvider, providerNames])

  const series = useMemo(() => [
    { name: totalLabel, color: 'purple.solid' },
    ...providerNames.map((name, index) => ({ name, color: COST_COLORS[index % COST_COLORS.length] })),
  ], [providerNames])

  const chart = useChart({ data: pivoted, series })

  return (
    <Chart.Root maxH="xs" chart={chart}>
      <AreaChart data={chart.data} responsive>
        <CartesianGrid stroke={chart.color('border.muted')} vertical={false} strokeDasharray="3 3" />
        <XAxis dataKey={chart.key('date')} axisLine={false} tickLine={false} tick={{ fontSize: 11 }} />
        <YAxis axisLine={false} tickLine={false} tickFormatter={(value) => `$${Number(value).toFixed(4)}`} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} content={<Chart.Tooltip />} />
        <Legend content={<Chart.Legend />} />

        {series.map((item, index) => (
          <defs key={`cost-grad-${index}`}>
            <Chart.Gradient
              id={`cost-grad-${index}`}
              stops={[
                { offset: '0%', color: item.color, opacity: 0.4 },
                { offset: '100%', color: item.color, opacity: 0.05 },
              ]}
            />
          </defs>
        ))}

        {series.map((item, index) => (
          <Area
            key={item.name}
            type="monotone"
            isAnimationActive={false}
            dataKey={chart.key(item.name)}
            fill={`url(#cost-grad-${index})`}
            stroke={chart.color(item.color)}
            strokeWidth={2}
            name={item.name}
          />
        ))}
      </AreaChart>
    </Chart.Root>
  )
}

export function ProviderBarChart({ data }: { data: UsageQueryResult['byProvider'] }) {
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
        <YAxis tickFormatter={(value) => fmtTokens(value)} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} />
        <Legend />
        <Bar dataKey="inputTokens" fill="#3b82f6" name="输入 Token" />
        <Bar dataKey="outputTokens" fill="#22c55e" name="输出 Token" />
      </BarChart>
    </Chart.Root>
  )
}

export function SourceBarChart({ data }: { data: UsageQueryResult['bySource'] }) {
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
        <YAxis tickFormatter={(value) => fmtTokens(value)} tick={{ fontSize: 11 }} />
        <Tooltip cursor={false} animationDuration={100} />
        <Legend />
        <Bar dataKey="inputTokens" fill="#3b82f6" name="输入 Token" />
        <Bar dataKey="outputTokens" fill="#22c55e" name="输出 Token" />
      </BarChart>
    </Chart.Root>
  )
}

export function AgentUsageTable({
  data,
  agents,
}: {
  data: UsageQueryResult['byAgent']
  agents: AgentConfig[]
}) {
  const agentMap = useMemo(() => {
    const map = new Map<string, AgentConfig>()
    agents.forEach((agent) => map.set(agent.id, agent))
    return map
  }, [agents])

  if (data.length === 0) {
    return <Box py="4" color="gray.400" textAlign="center">暂无按 Agent 分组的数据（需重新查询启用 agentId 追踪后的数据）</Box>
  }

  return (
    <Table.ScrollArea>
      <Table.Root size="sm">
        <Table.Header>
          <Table.Row>
            <Table.ColumnHeader>Agent</Table.ColumnHeader>
            <Table.ColumnHeader textAlign="right">输入 Token</Table.ColumnHeader>
            <Table.ColumnHeader textAlign="right">输出 Token</Table.ColumnHeader>
            <Table.ColumnHeader textAlign="right">费用 (USD)</Table.ColumnHeader>
            <Table.ColumnHeader>月度预算进度</Table.ColumnHeader>
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {data.map((row) => {
            const agent = agentMap.get(row.agentId)
            const agentName = (agent?.name ?? row.agentId) || '（未关联）'
            const budget = agent?.monthlyBudgetUsd
            const pct = budget && budget > 0 ? Math.min((row.estimatedCostUsd / budget) * 100, 100) : null
            const budgetColor = pct === null ? 'gray' : pct >= 100 ? 'red' : pct >= 80 ? 'orange' : 'green'

            return (
              <Table.Row key={row.agentId || 'unknown'}>
                <Table.Cell fontWeight="medium">{agentName}</Table.Cell>
                <Table.Cell textAlign="right">{fmtTokens(row.inputTokens)}</Table.Cell>
                <Table.Cell textAlign="right">{fmtTokens(row.outputTokens)}</Table.Cell>
                <Table.Cell textAlign="right">${row.estimatedCostUsd.toFixed(4)}</Table.Cell>
                <Table.Cell minW="160px">
                  {pct !== null ? (
                    <Box>
                      <HStack mb="1" justify="space-between">
                        <Text fontSize="xs" color="gray.500">${row.estimatedCostUsd.toFixed(4)} / ${budget!.toFixed(2)}</Text>
                        <Text fontSize="xs" color={`${budgetColor}.500`} fontWeight="medium">{pct.toFixed(1)}%</Text>
                      </HStack>
                      <Progress.Root value={pct} colorPalette={budgetColor} size="sm">
                        <Progress.Track><Progress.Range /></Progress.Track>
                      </Progress.Root>
                    </Box>
                  ) : (
                    <Text fontSize="xs" color="gray.400">未设置预算</Text>
                  )}
                </Table.Cell>
              </Table.Row>
            )
          })}
        </Table.Body>
      </Table.Root>
    </Table.ScrollArea>
  )
}
