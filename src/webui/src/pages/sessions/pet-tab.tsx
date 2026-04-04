import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Input, Textarea,
  Spinner, Switch, Tabs, Table, IconButton,
} from '@chakra-ui/react'
import { RefreshCw, Save } from 'lucide-react'
import { AreaChart, Area, ResponsiveContainer, Tooltip as RechartsTooltip } from 'recharts'
import { toaster } from '@/components/ui/toaster'
import { eventBus } from '@/services/eventBus'
import {
  getPetStatus,
  getPetEmotionHistory,
  updatePetConfig,
  getPetJournal,
  getPetKnowledge,
  getPetPrompts,
  updatePetPrompts,
  type PetStatusDto,
  type PetKnowledgeDto,
  type PetPromptsDto,
  type SessionInfo,
  type UpdatePetConfigRequest,
} from '@/api/gateway'

// ── 常量 ──────────────────────────────────────────────────────────────────

const BEHAVIOR_STATE_LABELS: Record<string, string> = {
  Idle: '空闲',
  Learning: '学习中',
  Organizing: '整理中',
  Resting: '休息中',
  Reflecting: '反思中',
  Social: '社交中',
  Panic: '异常',
  Dispatching: '调度中',
}

const BEHAVIOR_STATE_COLORS: Record<string, string> = {
  Idle: 'gray',
  Learning: 'blue',
  Organizing: 'purple',
  Resting: 'teal',
  Reflecting: 'cyan',
  Social: 'green',
  Panic: 'red',
  Dispatching: 'orange',
}

const DIMENSION_COLORS: Record<string, string> = {
  alertness: '#3182ce',
  mood: '#38a169',
  curiosity: '#d69e2e',
  confidence: '#e53e3e',
}

// ── Pet 状态卡片 ──────────────────────────────────────────────────────────

function PetStatusCard({ status, moodHistory }: { status: PetStatusDto; moodHistory: number[] }) {
  const stateLabel = BEHAVIOR_STATE_LABELS[status.behaviorState] ?? status.behaviorState
  const stateColor = BEHAVIOR_STATE_COLORS[status.behaviorState] ?? 'gray'

  const dimensionBadge = (label: string, value: number, color: string) => (
    <VStack key={label} gap="0" align="center" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="80px">
      <Text fontSize="xs" color="var(--mc-text-muted)">{label}</Text>
      <Text fontWeight="bold" fontSize="xl" color={color}>{value}</Text>
      <Box w="100%" bg="var(--mc-border)" rounded="full" h="2" mt="1">
        <Box bg={color} h="2" rounded="full" style={{ width: `${value}%` }} />
      </Box>
    </VStack>
  )

  return (
    <Box>
      <HStack gap="3" mb="3" flexWrap="wrap">
        <HStack>
          <Text fontSize="sm" color="var(--mc-text-muted)">状态：</Text>
          <Badge colorPalette={stateColor} size="md">{stateLabel}</Badge>
        </HStack>
        <HStack>
          <Text fontSize="sm" color="var(--mc-text-muted)">启用：</Text>
          <Badge colorPalette={status.enabled ? 'green' : 'gray'} size="sm">
            {status.enabled ? '是' : '否'}
          </Badge>
        </HStack>
        {status.lastHeartbeatAt && (
          <Text fontSize="xs" color="var(--mc-text-muted)">
            上次心跳：{new Date(status.lastHeartbeatAt).toLocaleString('zh-CN')}
          </Text>
        )}
      </HStack>

      <HStack gap="2" mb="3" flexWrap="wrap">
        {dimensionBadge('警觉度', status.emotion.alertness, DIMENSION_COLORS.alertness)}
        {dimensionBadge('心情', status.emotion.mood, DIMENSION_COLORS.mood)}
        {dimensionBadge('好奇心', status.emotion.curiosity, DIMENSION_COLORS.curiosity)}
        {dimensionBadge('信心', status.emotion.confidence, DIMENSION_COLORS.confidence)}
      </HStack>

      {moodHistory.length >= 1 && (
        <Box mb="3">
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">近 7 天心情趋势</Text>
          <ResponsiveContainer width="100%" height={52}>
            <AreaChart data={moodHistory.map((v, i) => ({ i, v }))} margin={{ top: 2, right: 2, left: 2, bottom: 2 }}>
              <defs>
                <linearGradient id="moodGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={DIMENSION_COLORS.mood} stopOpacity={0.35} />
                  <stop offset="95%" stopColor={DIMENSION_COLORS.mood} stopOpacity={0} />
                </linearGradient>
              </defs>
              <Area
                type="monotone"
                dataKey="v"
                stroke={DIMENSION_COLORS.mood}
                fill="url(#moodGrad)"
                strokeWidth={1.5}
                dot={false}
                isAnimationActive={false}
              />
              <RechartsTooltip
                formatter={(v) => [`${v ?? ''}`, '心情']}
                contentStyle={{ fontSize: 11, padding: '2px 8px' }}
              />
            </AreaChart>
          </ResponsiveContainer>
        </Box>
      )}

      {status.rateLimit && (
        <HStack fontSize="xs" color="var(--mc-text-muted)" gap="3">
          <Text>速率限制：{status.rateLimit.usedCalls}/{status.rateLimit.maxCalls}</Text>
          {status.rateLimit.isExhausted && (
            <Badge colorPalette="red" size="xs">已耗尽</Badge>
          )}
        </HStack>
      )}
    </Box>
  )
}

// ── Pet 配置面板 ──────────────────────────────────────────────────────────

function PetConfigPanel({
  session,
  status,
  onUpdated,
}: {
  session: SessionInfo
  status: PetStatusDto
  onUpdated: () => void
}) {
  const [enabled, setEnabled] = useState(status.enabled)
  const [maxCalls, setMaxCalls] = useState(status.rateLimit?.maxCalls ?? 100)
  const [windowHours, setWindowHours] = useState(5)
  const [socialMode, setSocialMode] = useState(false)
  const [preferredProviderId, setPreferredProviderId] = useState('')
  const [saving, setSaving] = useState(false)

  const save = async () => {
    setSaving(true)
    try {
      const req: UpdatePetConfigRequest = {
        enabled,
        maxLlmCallsPerWindow: maxCalls,
        windowHours,
        socialMode,
        preferredProviderId: preferredProviderId || undefined,
      }
      await updatePetConfig(session.id, req)
      toaster.create({ type: 'success', title: 'Pet 配置已保存' })
      onUpdated()
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <VStack align="start" gap="3" p="3">
      <HStack justify="space-between" w="100%">
        <Text fontSize="sm">启用 Pet 编排</Text>
        <Switch.Root
          checked={enabled}
          onCheckedChange={(e) => setEnabled(e.checked)}
        >
          <Switch.HiddenInput />
          <Switch.Control>
            <Switch.Thumb />
          </Switch.Control>
        </Switch.Root>
      </HStack>

      <HStack justify="space-between" w="100%">
        <Text fontSize="sm">社交模式</Text>
        <Switch.Root
          checked={socialMode}
          onCheckedChange={(e) => setSocialMode(e.checked)}
        >
          <Switch.HiddenInput />
          <Switch.Control>
            <Switch.Thumb />
          </Switch.Control>
        </Switch.Root>
      </HStack>

      <HStack w="100%" gap="3">
        <Box flex="1">
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">每窗口最大调用数</Text>
          <Input
            size="sm"
            type="number"
            min={1}
            value={maxCalls}
            onChange={(e) => setMaxCalls(parseInt(e.target.value, 10) || 100)}
          />
        </Box>
        <Box flex="1">
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">窗口时长（小时）</Text>
          <Input
            size="sm"
            type="number"
            min={0.5}
            step={0.5}
            value={windowHours}
            onChange={(e) => setWindowHours(parseFloat(e.target.value) || 5)}
          />
        </Box>
      </HStack>

      <Box w="100%">
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">首选 Provider ID（留空使用默认）</Text>
        <Input
          size="sm"
          placeholder="可选"
          value={preferredProviderId}
          onChange={(e) => setPreferredProviderId(e.target.value)}
        />
      </Box>

      <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
        <Save size={14} />
        保存配置
      </Button>
    </VStack>
  )
}

// ── Pet 行为日志 ──────────────────────────────────────────────────────────

function PetJournalPanel({ session }: { session: SessionInfo }) {
  const [entries, setEntries] = useState<string[]>([])
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const res = await getPetJournal(session.id, 100)
      setEntries(res.entries)
    } catch {
      toaster.create({ type: 'error', title: '加载行为日志失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id])

  useEffect(() => { load() }, [load])

  if (loading) return <Box p="3"><Spinner size="sm" /></Box>

  return (
    <Box p="3">
      <HStack justify="space-between" mb="2">
        <Text fontSize="xs" color="var(--mc-text-muted)">行为日志（最近 {entries.length} 条）</Text>
        <IconButton aria-label="刷新" size="xs" variant="ghost" onClick={load}>
          <RefreshCw size={14} />
        </IconButton>
      </HStack>
      {entries.length === 0 ? (
        <Text fontSize="sm" color="var(--mc-text-muted)" textAlign="center" py="4">暂无行为日志</Text>
      ) : (
        <Box maxH="400px" overflowY="auto" borderWidth="1px" rounded="md">
          <Table.Root size="sm">
            <Table.Body>
              {entries.map((entry, i) => {
                let parsed: Record<string, unknown> | null = null
                try { parsed = JSON.parse(entry) } catch { /* rawtext */ }
                return (
                  <Table.Row key={i}>
                    <Table.Cell>
                      {parsed ? (
                        <VStack align="start" gap="0">
                          <HStack gap="2">
                            {Boolean(parsed.behaviorState) && (
                              <Badge size="xs" colorPalette={BEHAVIOR_STATE_COLORS[String(parsed.behaviorState)] ?? 'gray'}>
                                {BEHAVIOR_STATE_LABELS[String(parsed.behaviorState)] ?? String(parsed.behaviorState)}
                              </Badge>
                            )}
                            {Boolean(parsed.reason) && (
                              <Text fontSize="xs">{String(parsed.reason)}</Text>
                            )}
                          </HStack>
                          {Boolean(parsed.timestamp) && (
                            <Text fontSize="xs" color="var(--mc-text-muted)">
                              {new Date(String(parsed.timestamp)).toLocaleString('zh-CN')}
                            </Text>
                          )}
                        </VStack>
                      ) : (
                        <Text fontSize="xs" fontFamily="mono">{entry}</Text>
                      )}
                    </Table.Cell>
                  </Table.Row>
                )
              })}
            </Table.Body>
          </Table.Root>
        </Box>
      )}
    </Box>
  )
}

// ── Pet 知识库概要 ────────────────────────────────────────────────────────

function PetKnowledgePanel({ session }: { session: SessionInfo }) {
  const [knowledge, setKnowledge] = useState<PetKnowledgeDto | null>(null)
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await getPetKnowledge(session.id)
      setKnowledge(data)
    } catch {
      toaster.create({ type: 'error', title: '加载知识库信息失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id])

  useEffect(() => { load() }, [load])

  if (loading) return <Box p="3"><Spinner size="sm" /></Box>

  return (
    <Box p="3">
      <HStack justify="space-between" mb="2">
        <Text fontSize="sm" fontWeight="medium">Pet 知识库</Text>
        <IconButton aria-label="刷新" size="xs" variant="ghost" onClick={load}>
          <RefreshCw size={14} />
        </IconButton>
      </HStack>
      {knowledge ? (
        <HStack gap="4">
          <VStack gap="0" align="center" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="100px">
            <Text fontSize="xs" color="var(--mc-text-muted)">知识片段</Text>
            <Text fontWeight="bold" fontSize="xl">{knowledge.chunkCount}</Text>
          </VStack>
          <VStack gap="0" align="center" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="100px">
            <Text fontSize="xs" color="var(--mc-text-muted)">数据库大小</Text>
            <Text fontWeight="bold" fontSize="xl">
              {knowledge.dbSizeBytes < 1024 * 1024
                ? `${(knowledge.dbSizeBytes / 1024).toFixed(1)} KB`
                : `${(knowledge.dbSizeBytes / 1024 / 1024).toFixed(1)} MB`}
            </Text>
          </VStack>
        </HStack>
      ) : (
        <Text fontSize="sm" color="var(--mc-text-muted)">暂无数据</Text>
      )}
    </Box>
  )
}

// ── Pet 提示词编辑 ────────────────────────────────────────────────────────

function PetPromptsPanel({ session }: { session: SessionInfo }) {
  const [prompts, setPrompts] = useState<PetPromptsDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  // editable copies
  const [persona, setPersona] = useState('')
  const [tone, setTone] = useState('')
  const [language, setLanguage] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await getPetPrompts(session.id)
      setPrompts(data)
      setPersona(data.personality.persona)
      setTone(data.personality.tone)
      setLanguage(data.personality.language)
    } catch {
      toaster.create({ type: 'error', title: '加载提示词失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id])

  useEffect(() => { load() }, [load])

  const save = async () => {
    setSaving(true)
    try {
      await updatePetPrompts(session.id, {
        personality: { persona, tone, language },
      })
      toaster.create({ type: 'success', title: '提示词已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="3"><Spinner size="sm" /></Box>
  if (!prompts) return <Box p="3"><Text fontSize="sm" color="var(--mc-text-muted)">暂无提示词</Text></Box>

  return (
    <Flex direction="column" p="3" gap="3">
      <HStack justify="space-between">
        <Text fontSize="sm" fontWeight="medium">人格提示词</Text>
        <IconButton aria-label="刷新" size="xs" variant="ghost" onClick={load}>
          <RefreshCw size={14} />
        </IconButton>
      </HStack>

      <Box>
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">Persona</Text>
        <Textarea
          size="sm"
          fontFamily="mono"
          fontSize="sm"
          resize="vertical"
          minH="80px"
          value={persona}
          onChange={(e) => setPersona(e.target.value)}
          spellCheck={false}
        />
      </Box>

      <HStack gap="3">
        <Box flex="1">
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">Tone</Text>
          <Input size="sm" value={tone} onChange={(e) => setTone(e.target.value)} />
        </Box>
        <Box flex="1">
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">Language</Text>
          <Input size="sm" value={language} onChange={(e) => setLanguage(e.target.value)} />
        </Box>
      </HStack>

      <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
        <Save size={14} />
        保存人格提示词
      </Button>

      {/* 调度规则（只读展示） */}
      <Box mt="2">
        <Text fontSize="sm" fontWeight="medium" mb="1">调度规则</Text>
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">
          默认策略：{prompts.dispatchRules.defaultStrategy}
        </Text>
        {prompts.dispatchRules.rules.length > 0 ? (
          <Table.Root size="sm" variant="outline">
            <Table.Header>
              <Table.Row>
                <Table.ColumnHeader>匹配模式</Table.ColumnHeader>
                <Table.ColumnHeader>首选模型类型</Table.ColumnHeader>
                <Table.ColumnHeader>备注</Table.ColumnHeader>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {prompts.dispatchRules.rules.map((rule, i) => (
                <Table.Row key={i}>
                  <Table.Cell><Text fontSize="xs">{rule.pattern}</Text></Table.Cell>
                  <Table.Cell><Text fontSize="xs">{rule.preferredModelType}</Text></Table.Cell>
                  <Table.Cell><Text fontSize="xs" color="var(--mc-text-muted)">{rule.notes}</Text></Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table.Root>
        ) : (
          <Text fontSize="xs" color="var(--mc-text-muted)">暂无自定义调度规则</Text>
        )}
      </Box>

      {/* 学习兴趣（只读展示） */}
      <Box mt="2">
        <Text fontSize="sm" fontWeight="medium" mb="1">学习兴趣</Text>
        {prompts.knowledgeInterests.topics.length > 0 ? (
          <HStack gap="2" flexWrap="wrap">
            {prompts.knowledgeInterests.topics.map((topic, i) => (
              <Badge key={i} variant="outline" size="sm" title={topic.description}>
                {topic.name} ({topic.priority})
              </Badge>
            ))}
          </HStack>
        ) : (
          <Text fontSize="xs" color="var(--mc-text-muted)">暂无学习兴趣</Text>
        )}
      </Box>
    </Flex>
  )
}

// ── 主组件：PetTab ────────────────────────────────────────────────────────

export function PetTab({ session }: { session: SessionInfo }) {
  const [status, setStatus] = useState<PetStatusDto | null>(null)
  const [moodHistory, setMoodHistory] = useState<number[]>([])
  const [loading, setLoading] = useState(false)
  const [petNotFound, setPetNotFound] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setPetNotFound(false)
    try {
      const data = await getPetStatus(session.id)
      setStatus(data)

      // 加载 7 天心情历史（忽略失败，不影响主面板）
      try {
        const now = Date.now()
        const from = now - 7 * 24 * 60 * 60 * 1000
        const snapshots = await getPetEmotionHistory(session.id, from, now)
        // 按天聚合：每天取最后一条 mood 值
        const byDay: Record<string, number> = {}
        for (const s of snapshots) {
          const day = new Date(s.recordedAtMs).toISOString().slice(0, 10)
          byDay[day] = s.state.mood
        }
        const values = Object.values(byDay)
        // 无历史时用当前 mood 作为初始单点，确保图表可见
        setMoodHistory(values.length > 0 ? values : [data.emotion.mood])
      } catch {
        // 无历史数据时静默忽略
      }
    } catch (err: unknown) {
      // 404 = Pet 未创建
      if (err && typeof err === 'object' && 'response' in err) {
        const resp = (err as { response?: { status?: number } }).response
        if (resp?.status === 404) {
          setPetNotFound(true)
          setStatus(null)
          return
        }
      }
      toaster.create({ type: 'error', title: '加载 Pet 状态失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id])

  useEffect(() => { load() }, [load])

  // 监听 SignalR Pet 事件
  useEffect(() => {
    const onStateChanged = (payload: unknown) => {
      const data = payload as { sessionId?: string }
      if (data?.sessionId === session.id) load()
    }
    eventBus.on('pet:stateChanged', onStateChanged)
    eventBus.on('pet:message', onStateChanged)
    return () => {
      eventBus.off('pet:stateChanged', onStateChanged)
      eventBus.off('pet:message', onStateChanged)
    }
  }, [session.id, load])

  if (loading && !status) return <Box p="4"><Spinner /></Box>

  if (petNotFound) {
    return (
      <Flex direction="column" align="center" justify="center" p="6" gap="2">
        <Text fontSize="sm" color="var(--mc-text-muted)">此会话尚未创建 Pet</Text>
        <Text fontSize="xs" color="var(--mc-text-muted)">批准会话后将自动创建 Pet 编排层</Text>
      </Flex>
    )
  }

  if (!status) return null

  return (
    <Flex direction="column" h="100%" overflow="auto">
      {/* 顶部状态卡片 */}
      <Box px="3" pt="3" pb="1" borderBottomWidth="1px">
        <HStack justify="space-between" mb="2">
          <Text fontSize="sm" fontWeight="medium">🐾 Pet 编排层</Text>
          <Button size="xs" variant="ghost" data-mc-refresh="true" loading={loading} onClick={load}>
            <RefreshCw size={14} />
          </Button>
        </HStack>
        <PetStatusCard status={status} moodHistory={moodHistory} />
      </Box>

      {/* 子 Tab */}
      <Tabs.Root defaultValue="config" flex="1" display="flex" flexDirection="column" overflow="hidden">
        <Tabs.List px="3">
          <Tabs.Trigger value="config">配置</Tabs.Trigger>
          <Tabs.Trigger value="journal">行为日志</Tabs.Trigger>
          <Tabs.Trigger value="knowledge">知识库</Tabs.Trigger>
          <Tabs.Trigger value="prompts">提示词</Tabs.Trigger>
        </Tabs.List>
        <Tabs.Content value="config" flex="1" overflow="auto" p="0">
          <PetConfigPanel session={session} status={status} onUpdated={load} />
        </Tabs.Content>
        <Tabs.Content value="journal" flex="1" overflow="auto" p="0">
          <PetJournalPanel session={session} />
        </Tabs.Content>
        <Tabs.Content value="knowledge" flex="1" overflow="auto" p="0">
          <PetKnowledgePanel session={session} />
        </Tabs.Content>
        <Tabs.Content value="prompts" flex="1" overflow="auto" p="0">
          <PetPromptsPanel session={session} />
        </Tabs.Content>
      </Tabs.Root>
    </Flex>
  )
}
