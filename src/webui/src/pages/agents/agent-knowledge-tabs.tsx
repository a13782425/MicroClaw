import { useState, useEffect } from 'react'
import {
  Box, Text, Badge, Button, HStack, VStack, Spinner,
} from '@chakra-ui/react'
import { Trash2 } from 'lucide-react'
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  getAgentEmotionCurrent,
  getAgentEmotionHistory,
  listAgentPainMemories,
  deleteAgentPainMemory,
  type AgentConfig,
  type EmotionStateDto,
  type EmotionSnapshotDto,
  type PainMemoryDto,
} from '@/api/gateway'
import {
  ChartPoint,
  DIMENSION_COLORS,
  DIMENSION_LABELS,
  SEVERITY_COLORS,
  SEVERITY_LABELS,
  toISODateLocal,
} from './agent-constants'

export function SafetyMemoryTab({ agent }: { agent: AgentConfig }) {
  const [memories, setMemories] = useState<PainMemoryDto[]>([])
  const [loading, setLoading] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

  const load = async () => {
    setLoading(true)
    try {
      const data = await listAgentPainMemories(agent.id)
      setMemories(data)
    } catch {
      // no-op
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [agent.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const handleDeleteClick = (memoryId: string) => {
    setPendingDeleteId(memoryId)
    setConfirmOpen(true)
  }

  const handleDeleteConfirm = async () => {
    if (!pendingDeleteId) return
    setDeletingId(pendingDeleteId)
    try {
      await deleteAgentPainMemory(agent.id, pendingDeleteId)
      setMemories((prev) => prev.filter((memory) => memory.id !== pendingDeleteId))
      toaster.create({ title: '已删除痛觉记忆', type: 'success' })
    } catch {
      toaster.create({ title: '删除失败', type: 'error' })
    } finally {
      setDeletingId(null)
      setPendingDeleteId(null)
      setConfirmOpen(false)
    }
  }

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">安全记忆</Text>
        <Button size="xs" variant="outline" onClick={load} loading={loading}>刷新</Button>
      </HStack>

      {loading && <Spinner size="sm" />}

      {!loading && memories.length === 0 && (
        <Box py="8" textAlign="center">
          <Text fontSize="sm" color="var(--mc-text-muted)">暂无痛觉记忆记录</Text>
          <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">当 Agent 执行操作失败且被记录痛觉时，将在此处显示</Text>
        </Box>
      )}

      {memories.map((memory) => (
        <Box key={memory.id} mb="3" p="3" borderWidth="1px" rounded="md" bg="var(--mc-surface-muted)">
          <HStack justify="space-between" mb="2" flexWrap="wrap" gap="2">
            <HStack gap="2">
              <Badge colorPalette={SEVERITY_COLORS[memory.severity] ?? 'gray'} size="sm">
                {SEVERITY_LABELS[memory.severity] ?? memory.severity}
              </Badge>
              <Badge variant="outline" size="sm">第 {memory.occurrenceCount} 次</Badge>
            </HStack>
            <Button
              size="xs"
              variant="ghost"
              colorPalette="red"
              loading={deletingId === memory.id}
              onClick={() => handleDeleteClick(memory.id)}
            >
              <Trash2 size={12} />
            </Button>
          </HStack>

          <VStack align="start" gap="1">
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">触发点</Text>
              <Text fontSize="sm">{memory.triggerDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">后果</Text>
              <Text fontSize="sm">{memory.consequenceDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">规避策略</Text>
              <Text fontSize="sm" color="var(--mc-link-color)">{memory.avoidanceStrategy}</Text>
            </Box>
            <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">
              最近发生：{new Date(memory.lastOccurredAtMs).toLocaleString('zh-CN')}
            </Text>
          </VStack>
        </Box>
      ))}

      <ConfirmDialog
        open={confirmOpen}
        onClose={() => { setConfirmOpen(false); setPendingDeleteId(null) }}
        onConfirm={handleDeleteConfirm}
        title="删除痛觉记忆"
        description="确认删除该条痛觉记忆？删除后无法恢复。"
        confirmText="删除"
        loading={deletingId !== null}
      />
    </Box>
  )
}

export function EmotionTab({ agent }: { agent: AgentConfig }) {
  const [current, setCurrent] = useState<EmotionStateDto | null>(null)
  const [history, setHistory] = useState<ChartPoint[]>([])
  const [loading, setLoading] = useState(false)
  const [days, setDays] = useState(7)

  const load = async () => {
    setLoading(true)
    try {
      const [cur] = await Promise.all([getAgentEmotionCurrent(agent.id)])
      setCurrent(cur)

      const now = Date.now()
      const from = now - days * 24 * 60 * 60 * 1000
      const snapshots: EmotionSnapshotDto[] = await getAgentEmotionHistory(agent.id, { from, to: now })
      const points: ChartPoint[] = snapshots.map((snapshot) => ({
        time: toISODateLocal(snapshot.recordedAtMs),
        alertness: snapshot.alertness,
        mood: snapshot.mood,
        curiosity: snapshot.curiosity,
        confidence: snapshot.confidence,
      }))
      setHistory(points)
    } catch {
      // no-op
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [agent.id, days]) // eslint-disable-line react-hooks/exhaustive-deps

  const dimensionBadge = (label: string, value: number, color: string) => (
    <VStack key={label} gap="0" align="center" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="90px">
      <Text fontSize="xs" color="var(--mc-text-muted)">{label}</Text>
      <Text fontWeight="bold" fontSize="xl" color={color}>{value}</Text>
      <Box w="100%" bg="var(--mc-border)" rounded="full" h="2" mt="1">
        <Box bg={color} h="2" rounded="full" style={{ width: `${value}%` }} />
      </Box>
    </VStack>
  )

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">情绪状态</Text>
        <HStack>
          {([7, 14, 30] as const).map((day) => (
            <Button key={day} size="xs" variant={days === day ? 'solid' : 'outline'} colorPalette="blue" onClick={() => setDays(day)}>
              {day}天
            </Button>
          ))}
          <Button size="xs" variant="outline" onClick={load} loading={loading}>刷新</Button>
        </HStack>
      </HStack>

      {current && (
        <HStack gap="2" mb="4" flexWrap="wrap">
          {dimensionBadge('警觉度', current.alertness, DIMENSION_COLORS.alertness)}
          {dimensionBadge('心情', current.mood, DIMENSION_COLORS.mood)}
          {dimensionBadge('好奇心', current.curiosity, DIMENSION_COLORS.curiosity)}
          {dimensionBadge('信心', current.confidence, DIMENSION_COLORS.confidence)}
        </HStack>
      )}

      {!current && !loading && (
        <Box mb="4">
          <Text fontSize="sm" color="var(--mc-text-muted)">暂无情绪数据（Agent 尚未运行）</Text>
        </Box>
      )}

      {loading && <Box mb="4"><Spinner size="sm" /></Box>}

      <Box borderWidth="1px" rounded="md" p="3">
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">过去 {days} 天情绪曲线</Text>
        {history.length === 0 && !loading ? (
          <Text fontSize="sm" color="var(--mc-text-muted)">暂无历史数据</Text>
        ) : (
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={history} margin={{ top: 5, right: 10, left: -20, bottom: 5 }}>
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="time" tick={{ fontSize: 10 }} interval="preserveStartEnd" />
              <YAxis domain={[0, 100]} tick={{ fontSize: 10 }} />
              <Tooltip formatter={(value, name) => [value, DIMENSION_LABELS[String(name)] ?? name]} />
              <Legend formatter={(name) => DIMENSION_LABELS[name] ?? name} />
              {Object.entries(DIMENSION_COLORS).map(([key, color]) => (
                <Line key={key} type="monotone" dataKey={key} stroke={color} dot={false} strokeWidth={2} />
              ))}
            </LineChart>
          </ResponsiveContainer>
        )}
      </Box>
    </Box>
  )
}
