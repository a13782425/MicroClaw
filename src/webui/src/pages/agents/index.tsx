import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Tabs, For, Em, Switch, createListCollection,
  Select, Portal,
} from '@chakra-ui/react'
import { Plus, Trash2, Bot } from 'lucide-react'
import {
  Dialog,
} from '@chakra-ui/react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  listAgents, createAgent, updateAgent, deleteAgent,
  listAgentTools, updateAgentToolSettings,
  listAgentDna, updateAgentDna,
  listSkills, listMcpServers,
  getAgentEmotionCurrent, getAgentEmotionHistory,
  listAgentPainMemories, deleteAgentPainMemory,
  type AgentConfig, type AgentCreateRequest, type ToolGroup,
  type ToolGroupConfig, type SkillConfig, type McpServerConfig,
  type AgentDnaFileInfo,
  type SubAgentInfo, listSubAgents,
  type EmotionStateDto, type EmotionSnapshotDto,
  type PainMemoryDto,
} from '@/api/gateway'
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer,
} from 'recharts'

// ─────────────────── 路由策略选项 ─────────────────────────────────────────────

const ROUTING_STRATEGY_OPTIONS = [
  { value: 'Default', label: '默认（使用默认 Provider）' },
  { value: 'QualityFirst', label: '质量优先' },
  { value: 'CostFirst', label: '成本优先' },
  { value: 'LatencyFirst', label: '延迟优先' },
]
const routingStrategyCollection = createListCollection({ items: ROUTING_STRATEGY_OPTIONS })

function routingStrategyLabel(strategy: string): string {
  return ROUTING_STRATEGY_OPTIONS.find((o) => o.value === strategy)?.label ?? strategy
}

// ─────────────────── 创建弹窗 ─────────────────────────────────────────────────

function CreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: () => void
}) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)

  const reset = () => { setName(''); setDescription('') }

  const submit = async () => {
    if (!name.trim()) return
    setSaving(true)
    try {
      const req: AgentCreateRequest = { name: name.trim(), description: description.trim() || undefined }
      await createAgent(req)
      toaster.create({ type: 'success', title: 'Agent 创建成功' })
      reset()
      onCreated()
      onClose()
    } catch {
      toaster.create({ type: 'error', title: '创建失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) { reset(); onClose() } }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="480px">
          <Dialog.Header>
            <Dialog.Title>添加 Agent</Dialog.Title>
          </Dialog.Header>
          <Dialog.Body>
            <VStack gap="3" align="stretch">
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">名称 <Text as="span" color="red.500">*</Text></Text>
                <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Agent 名称" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">描述</Text>
                <Textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="功能描述（可选）" />
              </Box>
            </VStack>
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>取消</Button>
            <Button colorPalette="blue" loading={saving} onClick={submit} disabled={!name.trim()}>创建</Button>
          </Dialog.Footer>
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ─────────────────── Tools Tab ───────────────────────────────────────────────

function ToolsTab({ agent }: { agent: AgentConfig }) {
  const [groups, setGroups] = useState<ToolGroup[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [dirty, setDirty] = useState(false)
  const localRef = useRef<ToolGroup[]>([])

  const load = async () => {
    setLoading(true)
    try {
      const res = await listAgentTools(agent.id)
      const copy = JSON.parse(JSON.stringify(res.groups)) as ToolGroup[]
      setGroups(copy)
      localRef.current = copy
      setDirty(false)
    } catch {
      toaster.create({ type: 'error', title: '加载工具失败' })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [agent.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const toggleGroup = (groupId: string, val: boolean) => {
    setGroups((prev) => prev.map((g) => g.id === groupId ? { ...g, isEnabled: val } : g))
    setDirty(true)
  }

  const toggleTool = (groupId: string, toolName: string, val: boolean) => {
    setGroups((prev) => prev.map((g) =>
      g.id === groupId
        ? { ...g, tools: g.tools.map((t) => t.name === toolName ? { ...t, isEnabled: val } : t) }
        : g,
    ))
    setDirty(true)
  }

  const save = async () => {
    setSaving(true)
    try {
      const configs: ToolGroupConfig[] = groups.map((g) => ({
        groupId: g.id,
        isEnabled: g.isEnabled,
        disabledToolNames: g.tools.filter((t) => !t.isEnabled).map((t) => t.name),
      }))
      await updateAgentToolSettings(agent.id, configs)
      toaster.create({ type: 'success', title: '工具设置已保存' })
      setDirty(false)
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">工具分组</Text>
        <HStack>
          {dirty && (
            <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
              保存工具设置
            </Button>
          )}
          <Button size="sm" variant="outline" onClick={load} loading={loading}>刷新</Button>
        </HStack>
      </HStack>
      {groups.length === 0 && (
        <Text color="gray.500" fontSize="sm">点击「刷新」加载工具列表</Text>
      )}
      <VStack gap="2" align="stretch">
        {groups.map((g) => (
          <Box key={g.id} borderWidth="1px" rounded="md" overflow="hidden">
            <HStack px="3" py="2" bg="gray.50" _dark={{ bg: 'gray.800' }}>
              <Switch.Root
                size="sm"
                checked={g.isEnabled}
                onCheckedChange={(e) => toggleGroup(g.id, e.checked)}
              >
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
              </Switch.Root>
              <Text fontSize="sm" fontWeight="medium" flex="1">{g.name}</Text>
              <Badge size="xs" colorPalette={g.type === 'builtin' ? 'orange' : 'blue'}>
                {g.type === 'builtin' ? '内置' : 'MCP'}
              </Badge>
              <Text fontSize="xs" color="gray.500">{g.tools.length} 个工具</Text>
            </HStack>
            <VStack gap="0" divideY="1px" align="stretch" px="3">
              {g.tools.map((t) => (
                <HStack key={t.name} py="2">
                  <Switch.Root
                    size="sm"
                    checked={t.isEnabled}
                    disabled={!g.isEnabled}
                    onCheckedChange={(e) => toggleTool(g.id, t.name, e.checked)}
                  >
                    <Switch.HiddenInput />
                    <Switch.Control><Switch.Thumb /></Switch.Control>
                  </Switch.Root>
                  <Box flex="1">
                    <Text fontSize="xs" fontWeight="medium">{t.name}</Text>
                    {t.description && <Text fontSize="xs" color="gray.500" truncate>{t.description}</Text>}
                  </Box>
                </HStack>
              ))}
            </VStack>
          </Box>
        ))}
      </VStack>
    </Box>
  )
}

// ─────────────────── MCP Tab ─────────────────────────────────────────────────

function McpTab({ agent, onUpdated }: { agent: AgentConfig; onUpdated: (a: AgentConfig) => void }) {
  const [servers, setServers] = useState<McpServerConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [disabledIds, setDisabledIds] = useState<string[]>(agent.disabledMcpServerIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify(disabledIds.sort()) !== JSON.stringify([...agent.disabledMcpServerIds].sort())

  useEffect(() => {
    setDisabledIds(agent.disabledMcpServerIds)
  }, [agent.id, agent.disabledMcpServerIds])

  useEffect(() => {
    setLoading(true)
    listMcpServers()
      .then(setServers)
      .catch(() => toaster.create({ type: 'error', title: '加载 MCP Servers 失败' }))
      .finally(() => setLoading(false))
  }, [])

  const toggle = (id: string, enabled: boolean) => {
    setDisabledIds((prev) => enabled ? prev.filter((x) => x !== id) : [...prev, id])
  }

  const save = async () => {
    setSaving(true)
    try {
      const res = await updateAgent({ id: agent.id, disabledMcpServerIds: disabledIds })
      onUpdated({ ...agent, disabledMcpServerIds: disabledIds })
      void res
      toaster.create({ type: 'success', title: 'MCP 引用已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">MCP Servers</Text>
        <Badge size="sm" colorPalette="blue">{servers.length - disabledIds.length} 个已启用</Badge>
      </HStack>
      {servers.length === 0 && (
        <Text color="gray.500" fontSize="sm">暂无全局 MCP Server，请先在 MCP 管理页创建</Text>
      )}
      <VStack gap="2" align="stretch">
        {servers.map((srv) => (
          <HStack key={srv.id} px="3" py="2" borderWidth="1px" rounded="md">
            <Switch.Root
              size="sm"
              checked={!disabledIds.includes(srv.id)}
              onCheckedChange={(e) => toggle(srv.id, e.checked)}
            >
              <Switch.HiddenInput />
              <Switch.Control><Switch.Thumb /></Switch.Control>
            </Switch.Root>
            <Badge size="xs" colorPalette="gray">{srv.transportType}</Badge>
            <Text fontSize="sm" flex="1">{srv.name}</Text>
            <Text fontSize="xs" color="gray.500" truncate maxW="200px">
              {srv.transportType === 'stdio'
                ? [srv.command, ...(srv.args ?? [])].join(' ')
                : srv.url}
            </Text>
            {!srv.isEnabled && <Badge size="xs" colorPalette="orange">全局已禁用</Badge>}
          </HStack>
        ))}
      </VStack>
      {isDirty && (
        <Box mt="3">
          <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
            保存 MCP 引用
          </Button>
        </Box>
      )}
    </Box>
  )
}

// ─────────────────── Skills Tab ──────────────────────────────────────────────

function SkillsTab({ agent, onUpdated }: { agent: AgentConfig; onUpdated: (a: AgentConfig) => void }) {
  const [skills, setSkills] = useState<SkillConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [disabledIds, setDisabledIds] = useState<string[]>(agent.disabledSkillIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify(disabledIds.sort()) !== JSON.stringify([...agent.disabledSkillIds].sort())

  useEffect(() => { setDisabledIds(agent.disabledSkillIds) }, [agent.id, agent.disabledSkillIds])

  useEffect(() => {
    setLoading(true)
    listSkills()
      .then(setSkills)
      .catch(() => toaster.create({ type: 'error', title: '加载技能失败' }))
      .finally(() => setLoading(false))
  }, [])

  const toggle = (id: string, enabled: boolean) => {
    setDisabledIds((prev) => enabled ? prev.filter((x) => x !== id) : [...prev, id])
  }

  const save = async () => {
    setSaving(true)
    try {
      await updateAgent({ id: agent.id, disabledSkillIds: disabledIds })
      onUpdated({ ...agent, disabledSkillIds: disabledIds })
      toaster.create({ type: 'success', title: '技能配置已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">技能绑定</Text>
        {isDirty && (
          <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
            保存技能配置
          </Button>
        )}
      </HStack>
      {skills.length === 0 && (
        <Text color="gray.500" fontSize="sm">暂无可用技能</Text>
      )}
      <VStack gap="2" align="stretch">
        {skills.map((sk) => (
          <HStack key={sk.id} px="3" py="2" borderWidth="1px" rounded="md">
            <input
              type="checkbox"
              checked={!disabledIds.includes(sk.id)}
              onChange={(e) => toggle(sk.id, e.target.checked)}
              style={{ width: 16, height: 16, cursor: 'pointer' }}
            />
            <Box flex="1">
              <Text fontSize="sm" fontWeight="medium">{sk.name}</Text>
              {sk.description && <Text fontSize="xs" color="gray.500">{sk.description}</Text>}
            </Box>
          </HStack>
        ))}
      </VStack>
    </Box>
  )
}

// ─────────────────── DNA Tab ─────────────────────────────────────────────────

function DnaTab({ agent }: { agent: AgentConfig }) {
  const [files, setFiles] = useState<AgentDnaFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [activeFile, setActiveFile] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listAgentDna(agent.id)
      setFiles(data)
      const init: Record<string, string> = {}
      data.forEach((f) => { init[f.fileName] = f.content })
      setEdits(init)
      if (data.length > 0 && !activeFile) setActiveFile(data[0].fileName)
    } catch {
      toaster.create({ type: 'error', title: '加载 Agent DNA 文件失败' })
    } finally {
      setLoading(false)
    }
  }, [agent.id, activeFile])

  useEffect(() => { load() }, [agent.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const save = async (fileName: string) => {
    setSaving(true)
    try {
      await updateAgentDna(agent.id, fileName, edits[fileName] ?? '')
      toaster.create({ type: 'success', title: '保存成功' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>
  if (files.length === 0) return <Box p="4"><Text color="gray.500" fontSize="sm">暂无 DNA 文件</Text></Box>

  const currentFile = files.find((f) => f.fileName === activeFile)

  return (
    <Flex h="100%" direction="column">
      <HStack gap="1" px="3" pt="3" flexWrap="wrap">
        {files.map((f) => (
          <Button
            key={f.fileName}
            size="xs"
            variant={activeFile === f.fileName ? 'solid' : 'outline'}
            colorPalette="blue"
            onClick={() => setActiveFile(f.fileName)}
          >
            {f.fileName.replace('.md', '')}
          </Button>
        ))}
      </HStack>

      {currentFile && (
        <Flex direction="column" flex="1" p="3" gap="2" overflow="hidden">
          {currentFile.description && (
            <Text fontSize="xs" color="gray.500">{currentFile.description}</Text>
          )}
          <Textarea
            flex="1"
            fontFamily="mono"
            fontSize="sm"
            resize="none"
            value={edits[currentFile.fileName] ?? ''}
            onChange={(e) => setEdits((prev) => ({ ...prev, [currentFile.fileName]: e.target.value }))}
            spellCheck={false}
          />
          <HStack>
            <Button size="sm" colorPalette="blue" loading={saving} onClick={() => save(currentFile.fileName)}>
              保存
            </Button>
            <Button size="sm" variant="outline" onClick={load} loading={loading}>刷新</Button>
          </HStack>
        </Flex>
      )}
    </Flex>
  )
}

// ─────────────────── SubAgents Tab ───────────────────────────────────────────

type SubAgentMode = 'all' | 'none' | 'select'

function SubAgentsTab({ agent, allAgents, onUpdated }: { agent: AgentConfig; allAgents: AgentConfig[]; onUpdated: (a: AgentConfig) => void }) {
  const [mode, setMode] = useState<SubAgentMode>(
    agent.allowedSubAgentIds === null ? 'all' : agent.allowedSubAgentIds.length === 0 ? 'none' : 'select',
  )
  const [selectedIds, setSelectedIds] = useState<string[]>(agent.allowedSubAgentIds ?? [])
  const [available, setAvailable] = useState<SubAgentInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  // 可选的子代理候选列表
  const candidates = allAgents.filter((a) => a.id !== agent.id && a.isEnabled)

  useEffect(() => {
    setMode(agent.allowedSubAgentIds === null ? 'all' : agent.allowedSubAgentIds.length === 0 ? 'none' : 'select')
    setSelectedIds(agent.allowedSubAgentIds ?? [])
  }, [agent.id, agent.allowedSubAgentIds])

  // 加载当前实际可调用的子代理
  useEffect(() => {
    setLoading(true)
    listSubAgents(agent.id)
      .then(setAvailable)
      .catch(() => toaster.create({ type: 'error', title: '加载可用子代理失败' }))
      .finally(() => setLoading(false))
  }, [agent.id])

  const isDirty = (() => {
    const currentValue = agent.allowedSubAgentIds
    if (mode === 'all') return currentValue !== null
    if (mode === 'none') return currentValue === null || currentValue.length !== 0
    // mode === 'select'
    if (currentValue === null) return true
    return JSON.stringify([...selectedIds].sort()) !== JSON.stringify([...currentValue].sort())
  })()

  const toggle = (id: string, checked: boolean) => {
    setSelectedIds((prev) => checked ? [...prev, id] : prev.filter((x) => x !== id))
  }

  const save = async () => {
    setSaving(true)
    try {
      const allowedSubAgentIds: string[] | null =
        mode === 'all' ? null : mode === 'none' ? [] : selectedIds
      await updateAgent({
        id: agent.id,
        allowedSubAgentIds,
        hasAllowedSubAgentIds: true,
      })
      onUpdated({ ...agent, allowedSubAgentIds })
      toaster.create({ type: 'success', title: '子代理权限已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">子代理调用权限</Text>
        {isDirty && (
          <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>保存</Button>
        )}
      </HStack>

      <VStack gap="2" align="stretch" mb="4">
        {(['all', 'none', 'select'] as const).map((m) => (
          <HStack
            key={m}
            px="3" py="2" borderWidth="1px" rounded="md" cursor="pointer"
            bg={mode === m ? 'blue.50' : undefined}
            _dark={{ bg: mode === m ? 'blue.900' : undefined }}
            onClick={() => setMode(m)}
          >
            <input type="radio" checked={mode === m} readOnly style={{ cursor: 'pointer' }} />
            <Text fontSize="sm">
              {m === 'all' ? '允许调用所有子代理（默认）' : m === 'none' ? '禁止调用任何子代理' : '仅允许调用指定子代理'}
            </Text>
          </HStack>
        ))}
      </VStack>

      {mode === 'select' && (
        <Box>
          <Text fontSize="xs" color="gray.500" mb="2">选择允许调用的子代理：</Text>
          {candidates.length === 0 ? (
            <Text fontSize="sm" color="gray.400">暂无其他已启用代理</Text>
          ) : (
            <VStack gap="1" align="stretch">
              {candidates.map((c) => (
                <HStack key={c.id} px="3" py="2" borderWidth="1px" rounded="md">
                  <input
                    type="checkbox"
                    checked={selectedIds.includes(c.id)}
                    onChange={(e) => toggle(c.id, e.target.checked)}
                    style={{ width: 16, height: 16, cursor: 'pointer' }}
                  />
                  <Box flex="1">
                    <HStack gap="1">
                      <Text fontSize="sm" fontWeight="medium">{c.name}</Text>
                      {c.isDefault && <Badge size="xs" colorPalette="yellow">DEFAULT</Badge>}
                    </HStack>
                    {c.description && <Text fontSize="xs" color="gray.500">{c.description}</Text>}
                  </Box>
                </HStack>
              ))}
            </VStack>
          )}
        </Box>
      )}

      {loading ? (
        <Box mt="4"><Spinner size="sm" /></Box>
      ) : (
        <Box mt="4">
          <Text fontSize="xs" color="gray.500" mb="1">当前实际可调用的子代理 ({available.length} 个)</Text>
          {available.length === 0 ? (
            <Text fontSize="sm" color="gray.400">无可调用子代理</Text>
          ) : (
            <HStack gap="1" flexWrap="wrap">
              {available.map((a) => (
                <Badge key={a.id} size="sm" colorPalette="blue">{a.name}</Badge>
              ))}
            </HStack>
          )}
        </Box>
      )}
    </Box>
  )
}

// ─────────────────── Emotion Tab ─────────────────────────────────────────────

const DIMENSION_COLORS = {
  alertness: '#3182ce',
  mood: '#38a169',
  curiosity: '#d69e2e',
  confidence: '#e53e3e',
}

const DIMENSION_LABELS: Record<string, string> = {
  alertness: '警觉度',
  mood: '心情',
  curiosity: '好奇心',
  confidence: '信心',
}

type ChartPoint = {
  time: string
  alertness: number
  mood: number
  curiosity: number
  confidence: number
}

function toISODateLocal(ms: number): string {
  const d = new Date(ms)
  return d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}

// ─────────────────── 安全记忆 Tab ────────────────────────────────────────────

const SEVERITY_COLORS: Record<string, string> = {
  Low: 'gray',
  Medium: 'yellow',
  High: 'orange',
  Critical: 'red',
}

const SEVERITY_LABELS: Record<string, string> = {
  Low: '低',
  Medium: '中',
  High: '高',
  Critical: '严重',
}

function SafetyMemoryTab({ agent }: { agent: AgentConfig }) {
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
      // 无数据时静默忽略
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
      setMemories((prev) => prev.filter((m) => m.id !== pendingDeleteId))
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
          <Text fontSize="sm" color="gray.400">暂无痛觉记忆记录</Text>
          <Text fontSize="xs" color="gray.400" mt="1">当 Agent 执行操作失败且被记录痛觉时，将在此处显示</Text>
        </Box>
      )}

      {memories.map((m) => (
        <Box
          key={m.id}
          mb="3"
          p="3"
          borderWidth="1px"
          rounded="md"
          bg="gray.50"
          _dark={{ bg: 'gray.800' }}
        >
          <HStack justify="space-between" mb="2" flexWrap="wrap" gap="2">
            <HStack gap="2">
              <Badge colorPalette={SEVERITY_COLORS[m.severity] ?? 'gray'} size="sm">
                {SEVERITY_LABELS[m.severity] ?? m.severity}
              </Badge>
              <Badge variant="outline" size="sm">第 {m.occurrenceCount} 次</Badge>
            </HStack>
            <Button
              size="xs"
              variant="ghost"
              colorPalette="red"
              loading={deletingId === m.id}
              onClick={() => handleDeleteClick(m.id)}
            >
              <Trash2 size={12} />
            </Button>
          </HStack>

          <VStack align="start" gap="1">
            <Box>
              <Text fontSize="xs" color="gray.500">触发点</Text>
              <Text fontSize="sm">{m.triggerDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="gray.500">后果</Text>
              <Text fontSize="sm">{m.consequenceDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="gray.500">规避策略</Text>
              <Text fontSize="sm" color="blue.600" _dark={{ color: 'blue.300' }}>{m.avoidanceStrategy}</Text>
            </Box>
            <Text fontSize="xs" color="gray.400" mt="1">
              最近发生：{new Date(m.lastOccurredAtMs).toLocaleString('zh-CN')}
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

// ─────────────────── 情绪 Tab ─────────────────────────────────────────────────

function EmotionTab({ agent }: { agent: AgentConfig }) {
  const [current, setCurrent] = useState<EmotionStateDto | null>(null)
  const [history, setHistory] = useState<ChartPoint[]>([])
  const [loading, setLoading] = useState(false)

  // 默认查询最近 7 天
  const [days, setDays] = useState(7)

  const load = async () => {
    setLoading(true)
    try {
      const [cur] = await Promise.all([
        getAgentEmotionCurrent(agent.id),
      ])
      setCurrent(cur)

      const now = Date.now()
      const from = now - days * 24 * 60 * 60 * 1000
      const snaps: EmotionSnapshotDto[] = await getAgentEmotionHistory(agent.id, { from, to: now })
      const points: ChartPoint[] = snaps.map((s) => ({
        time: toISODateLocal(s.recordedAtMs),
        alertness: s.alertness,
        mood: s.mood,
        curiosity: s.curiosity,
        confidence: s.confidence,
      }))
      setHistory(points)
    } catch {
      // 无情绪数据时静默忽略（如从未运行过）
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [agent.id, days]) // eslint-disable-line react-hooks/exhaustive-deps

  const dimensionBadge = (label: string, value: number, color: string) => (
    <VStack
      key={label}
      gap="0"
      align="center"
      bg="gray.50"
      _dark={{ bg: 'gray.800' }}
      p="3"
      rounded="md"
      minW="90px"
    >
      <Text fontSize="xs" color="gray.500">{label}</Text>
      <Text fontWeight="bold" fontSize="xl" color={color}>{value}</Text>
      <Box w="100%" bg="gray.200" _dark={{ bg: 'gray.700' }} rounded="full" h="2" mt="1">
        <Box
          bg={color}
          h="2"
          rounded="full"
          style={{ width: `${value}%` }}
        />
      </Box>
    </VStack>
  )

  return (
    <Box p="3">
      {/* 工具栏 */}
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">情绪状态</Text>
        <HStack>
          {([7, 14, 30] as const).map((d) => (
            <Button
              key={d}
              size="xs"
              variant={days === d ? 'solid' : 'outline'}
              colorPalette="blue"
              onClick={() => setDays(d)}
            >
              {d}天
            </Button>
          ))}
          <Button size="xs" variant="outline" onClick={load} loading={loading}>刷新</Button>
        </HStack>
      </HStack>

      {/* 当前情绪四维卡片 */}
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
          <Text fontSize="sm" color="gray.400">暂无情绪数据（Agent 尚未运行）</Text>
        </Box>
      )}

      {loading && <Box mb="4"><Spinner size="sm" /></Box>}

      {/* 历史折线图 */}
      <Box borderWidth="1px" rounded="md" p="3">
        <Text fontSize="xs" color="gray.500" mb="2">过去 {days} 天情绪曲线</Text>
        {history.length === 0 && !loading ? (
          <Text fontSize="sm" color="gray.400">暂无历史数据</Text>
        ) : (
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={history} margin={{ top: 5, right: 10, left: -20, bottom: 5 }}>
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="time" tick={{ fontSize: 10 }} interval="preserveStartEnd" />
              <YAxis domain={[0, 100]} tick={{ fontSize: 10 }} />
              <Tooltip
                formatter={(value, name) => [value, DIMENSION_LABELS[String(name)] ?? name]}
              />
              <Legend formatter={(name) => DIMENSION_LABELS[name] ?? name} />
              {Object.entries(DIMENSION_COLORS).map(([key, color]) => (
                <Line
                  key={key}
                  type="monotone"
                  dataKey={key}
                  stroke={color}
                  dot={false}
                  strokeWidth={2}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        )}
      </Box>
    </Box>
  )
}

// ─────────────────── 右侧详情 ─────────────────────────────────────────────────

function AgentDetail({
  agent,
  allAgents,
  onUpdated,
  onDeleted,
}: {
  agent: AgentConfig
  allAgents: AgentConfig[]
  onUpdated: (a: AgentConfig) => void
  onDeleted: (id: string) => void
}) {
  const [deleting, setDeleting] = useState(false)
  const [togglingEnabled, setTogglingEnabled] = useState(false)
  const [togglingA2A, setTogglingA2A] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const toggleEnabled = async (val: boolean) => {
    setTogglingEnabled(true)
    try {
      await updateAgent({ id: agent.id, isEnabled: val })
      onUpdated({ ...agent, isEnabled: val })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setTogglingEnabled(false)
    }
  }

  const toggleA2A = async (val: boolean) => {
    setTogglingA2A(true)
    try {
      await updateAgent({ id: agent.id, exposeAsA2A: val })
      onUpdated({ ...agent, exposeAsA2A: val })
      toaster.create({ type: 'success', title: val ? 'A2A 协议已开启' : 'A2A 协议已关闭' })
    } catch {
      toaster.create({ type: 'error', title: 'A2A 切换失败' })
    } finally {
      setTogglingA2A(false)
    }
  }

  const [savingRouting, setSavingRouting] = useState(false)
  const handleRoutingStrategyChange = async (strategy: string) => {
    setSavingRouting(true)
    try {
      await updateAgent({ id: agent.id, routingStrategy: strategy })
      onUpdated({ ...agent, routingStrategy: strategy })
      toaster.create({ type: 'success', title: '路由策略已更新' })
    } catch {
      toaster.create({ type: 'error', title: '路由策略更新失败' })
    } finally {
      setSavingRouting(false)
    }
  }

  const [budgetInput, setBudgetInput] = useState(
    agent.monthlyBudgetUsd != null ? String(agent.monthlyBudgetUsd) : ''
  )
  const [savingBudget, setSavingBudget] = useState(false)
  const handleBudgetSave = async () => {
    const parsed = budgetInput.trim() === '' ? null : parseFloat(budgetInput)
    if (budgetInput.trim() !== '' && (isNaN(parsed!) || parsed! < 0)) {
      toaster.create({ type: 'error', title: '预算须为非负数字或留空（不限制）' })
      return
    }
    setSavingBudget(true)
    try {
      await updateAgent({ id: agent.id, monthlyBudgetUsd: parsed, hasMonthlyBudgetUsd: true })
      onUpdated({ ...agent, monthlyBudgetUsd: parsed })
      toaster.create({ type: 'success', title: parsed == null ? '月度预算已清除' : '月度预算已更新' })
    } catch {
      toaster.create({ type: 'error', title: '月度预算更新失败' })
    } finally {
      setSavingBudget(false)
    }
  }

  const [contextWindowInput, setContextWindowInput] = useState(
    agent.contextWindowMessages != null ? String(agent.contextWindowMessages) : ''
  )
  const [savingContextWindow, setSavingContextWindow] = useState(false)
  const handleContextWindowSave = async () => {
    const parsed = contextWindowInput.trim() === '' ? null : parseInt(contextWindowInput, 10)
    if (contextWindowInput.trim() !== '' && (isNaN(parsed!) || parsed! < 1)) {
      toaster.create({ type: 'error', title: '上下文窗口须为正整数或留空（不限制）' })
      return
    }
    setSavingContextWindow(true)
    try {
      await updateAgent({ id: agent.id, contextWindowMessages: parsed, hasContextWindowMessages: true })
      onUpdated({ ...agent, contextWindowMessages: parsed })
      toaster.create({ type: 'success', title: parsed == null ? '上下文窗口限制已清除' : '上下文窗口已更新' })
    } catch {
      toaster.create({ type: 'error', title: '上下文窗口更新失败' })
    } finally {
      setSavingContextWindow(false)
    }
  }

  const handleDelete = async () => {
    setDeleting(true)
    try {
      await deleteAgent(agent.id)
      toaster.create({ type: 'success', title: 'Agent 已删除' })
      onDeleted(agent.id)
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleting(false)
      setConfirmOpen(false)
    }
  }

  return (
    <Flex direction="column" flex="1" overflow="hidden">
      {/* Header */}
      <HStack px="4" py="3" borderBottomWidth="1px" gap="3" flexWrap="wrap">
        <Box
          w="10" h="10" rounded="full" bg="blue.500"
          display="flex" alignItems="center" justifyContent="center"
        >
          <Text color="white" fontWeight="bold" fontSize="lg">
            {agent.name[0]?.toUpperCase()}
          </Text>
        </Box>
        <Box flex="1" minW="0">
          <HStack gap="2">
            <Text fontWeight="semibold" truncate>{agent.name}</Text>
            {agent.isDefault && <Badge colorPalette="yellow" size="sm">DEFAULT</Badge>}
          </HStack>
        </Box>
        <HStack>
          <Switch.Root
            size="sm"
            checked={agent.isEnabled}
            disabled={togglingEnabled}
            onCheckedChange={(e) => toggleEnabled(e.checked)}
          >
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="sm">{agent.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>
          <Switch.Root
            size="sm"
            checked={agent.exposeAsA2A}
            disabled={togglingA2A}
            onCheckedChange={(e) => toggleA2A(e.checked)}
          >
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="sm">A2A</Switch.Label>
          </Switch.Root>
          <Button
            size="sm"
            variant="outline"
            colorPalette="red"
            loading={deleting}
            disabled={agent.isDefault}
            title={agent.isDefault ? '默认 Agent 不可删除' : '删除 Agent'}
            onClick={() => setConfirmOpen(true)}
          >
            <Trash2 size={14} />
          </Button>
        </HStack>
      </HStack>

      <ConfirmDialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        onConfirm={handleDelete}
        title="删除 Agent"
        description={`确认删除 Agent「${agent.name}」？`}
        confirmText="删除"
        loading={deleting}
      />

      {/* Tabs */}
      <Tabs.Root defaultValue="overview" flex="1" overflow="hidden" display="flex" flexDirection="column">
        <Tabs.List px="3">
          <Tabs.Trigger value="overview">概览</Tabs.Trigger>
          <Tabs.Trigger value="dna">🧬 DNA</Tabs.Trigger>
          <Tabs.Trigger value="sub-agents">子代理</Tabs.Trigger>
          <Tabs.Trigger value="tools">工具</Tabs.Trigger>
          <Tabs.Trigger value="mcp">MCP</Tabs.Trigger>
          <Tabs.Trigger value="skills">技能</Tabs.Trigger>
          <Tabs.Trigger value="emotion">🧠 情绪</Tabs.Trigger>
          <Tabs.Trigger value="safety">🔴 安全记忆</Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="overview" flex="1" overflowY="auto">
          <Box p="4">
            <HStack gap="4" mb="4" flexWrap="wrap">
              <VStack gap="0" align="start" bg="gray.50" _dark={{ bg: 'gray.800' }} p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="gray.500">禁用 MCP</Text>
                <Text fontWeight="semibold">{agent.disabledMcpServerIds.length} 个</Text>
              </VStack>
              <VStack gap="0" align="start" bg="gray.50" _dark={{ bg: 'gray.800' }} p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="gray.500">禁用技能</Text>
                <Text fontWeight="semibold">{agent.disabledSkillIds.length} 个</Text>
              </VStack>
              <VStack gap="0" align="start" bg="gray.50" _dark={{ bg: 'gray.800' }} p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="gray.500">状态</Text>
                <Badge colorPalette={agent.isEnabled ? 'green' : 'gray'} size="sm">
                  {agent.isEnabled ? '启用' : '停用'}
                </Badge>
              </VStack>
            </HStack>
            <Box>
              <Text fontSize="xs" color="gray.500" mb="1">描述</Text>
              {agent.description
                ? <Text fontSize="sm" whiteSpace="pre-wrap">{agent.description}</Text>
                : <Text fontSize="sm" color="gray.400">（未设置）</Text>
              }
            </Box>

            {/* 路由策略配置 */}
            <Box mt="4">
              <Text fontSize="xs" color="gray.500" mb="1">Provider 路由策略</Text>
              <Text fontSize="xs" color="gray.400" mb="2">当会话未绑定具体 Provider 时，按此策略从已启用的 Provider 中自动选择。</Text>
              <Select.Root
                value={[agent.routingStrategy ?? 'Default']}
                onValueChange={(v) => handleRoutingStrategyChange(v.value[0])}
                collection={routingStrategyCollection}
                size="sm"
                disabled={savingRouting}
              >
                <Select.Trigger maxW="260px">
                  <Select.ValueText placeholder="选择路由策略" />
                </Select.Trigger>
                <Portal>
                  <Select.Positioner>
                    <Select.Content>
                      {ROUTING_STRATEGY_OPTIONS.map((o) => (
                        <Select.Item key={o.value} item={o}>{o.label}</Select.Item>
                      ))}
                    </Select.Content>
                  </Select.Positioner>
                </Portal>
              </Select.Root>
              {savingRouting && <Text fontSize="xs" color="gray.400" mt="1">保存中…</Text>}
            </Box>
            {/* 月度预算配置 */}
            <Box mt="4">
              <Text fontSize="xs" color="gray.500" mb="1">月度预算上限（USD）</Text>
              <Text fontSize="xs" color="gray.400" mb="2">超过 80% 记录预警日志，超过 100% 记录超限日志。留空表示不限制。</Text>
              <HStack>
                <Input
                  size="sm"
                  maxW="160px"
                  type="number"
                  min="0"
                  step="0.01"
                  placeholder="不限制"
                  value={budgetInput}
                  onChange={(e) => setBudgetInput(e.target.value)}
                />
                <Button size="sm" colorPalette="blue" loading={savingBudget} onClick={handleBudgetSave}>
                  保存
                </Button>
              </HStack>
            </Box>
            {/* 上下文窗口配置 */}
            <Box mt="4">
              <Text fontSize="xs" color="gray.500" mb="1">上下文窗口（条）</Text>
              <Text fontSize="xs" color="gray.400" mb="2">发送给 LLM 的最近消息条数。配合 RAG 使用时建议设为 20-50，留空表示全量历史（不限制）。</Text>
              <HStack>
                <Input
                  size="sm"
                  maxW="160px"
                  type="number"
                  min="1"
                  step="1"
                  placeholder="不限制"
                  value={contextWindowInput}
                  onChange={(e) => setContextWindowInput(e.target.value)}
                />
                <Button size="sm" colorPalette="blue" loading={savingContextWindow} onClick={handleContextWindowSave}>
                  保存
                </Button>
              </HStack>
            </Box>
            {agent.exposeAsA2A && (
              <Box mt="4">
                <Text fontSize="xs" color="gray.500" mb="1">A2A 端点</Text>
                <Box
                  bg="gray.50" _dark={{ bg: 'gray.800' }} p="2" rounded="md"
                  fontFamily="mono" fontSize="xs"
                  wordBreak="break-all"
                  cursor="text"
                  userSelect="all"
                >
                  {`${window.location.origin}/a2a/agent/${agent.id}`}
                </Box>
              </Box>
            )}
          </Box>
        </Tabs.Content>

        <Tabs.Content value="tools" flex="1" overflowY="auto" p="0">
          <ToolsTab agent={agent} />
        </Tabs.Content>

        <Tabs.Content value="sub-agents" flex="1" overflowY="auto" p="0">
          <SubAgentsTab agent={agent} allAgents={allAgents} onUpdated={onUpdated} />
        </Tabs.Content>

        <Tabs.Content value="dna" flex="1" overflowY="auto" p="0">
          <DnaTab agent={agent} />
        </Tabs.Content>

        <Tabs.Content value="mcp" flex="1" overflowY="auto" p="0">
          <McpTab agent={agent} onUpdated={onUpdated} />
        </Tabs.Content>

        <Tabs.Content value="skills" flex="1" overflowY="auto" p="0">
          <SkillsTab agent={agent} onUpdated={onUpdated} />
        </Tabs.Content>

        <Tabs.Content value="emotion" flex="1" overflowY="auto" p="0">
          <EmotionTab agent={agent} />
        </Tabs.Content>

        <Tabs.Content value="safety" flex="1" overflowY="auto" p="0">
          <SafetyMemoryTab agent={agent} />
        </Tabs.Content>
      </Tabs.Root>
    </Flex>
  )
}

// ──────────────────────────── 主页面 ─────────────────────────────────────────

export default function AgentsPage() {
  const [agents, setAgents] = useState<AgentConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<AgentConfig | null>(null)
  const [showCreate, setShowCreate] = useState(false)
  const loadedRef = useRef(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listAgents()
      setAgents(data)
      setSelected((prev) => prev ? (data.find((a) => a.id === prev.id) ?? null) : null)
    } catch {
      toaster.create({ type: 'error', title: '加载 Agent 列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (!loadedRef.current) {
      loadedRef.current = true
      load()
    }
  }, [load])

  const handleUpdated = (updated: AgentConfig) => {
    setAgents((prev) => prev.map((a) => (a.id === updated.id ? updated : a)))
    setSelected(updated)
  }

  const handleDeleted = (id: string) => {
    setAgents((prev) => prev.filter((a) => a.id !== id))
    setSelected(null)
  }

  return (
    <Flex h="100%" overflow="hidden">
      {/* 左侧 */}
      <Flex direction="column" w="260px" minW="260px" borderRightWidth="1px" overflow="hidden">
        <HStack px="3" py="2" borderBottomWidth="1px" justify="space-between">
          <Text fontWeight="semibold" fontSize="sm">代理</Text>
          <Button size="xs" colorPalette="blue" onClick={() => setShowCreate(true)}>
            <Plus size={14} />
          </Button>
        </HStack>
        <Box flex="1" overflowY="auto">
          {loading && agents.length === 0 && (
            <Box p="6" textAlign="center"><Spinner /></Box>
          )}
          {!loading && agents.length === 0 && (
            <Box p="6" textAlign="center">
              <Text color="gray.500" fontSize="sm">暂无 Agent</Text>
            </Box>
          )}
          <For each={agents}>
            {(a) => {
              const isActive = selected?.id === a.id
              return (
                <HStack
                  key={a.id}
                  px="3" py="2"
                  cursor="pointer"
                  borderBottomWidth="1px"
                  bg={isActive ? 'blue.50' : undefined}
                  _dark={{ bg: isActive ? 'blue.900' : undefined }}
                  _hover={{ bg: isActive ? undefined : 'gray.50', _dark: { bg: isActive ? undefined : 'gray.800' } }}
                  onClick={() => setSelected(a)}
                >
                  <Box
                    w="8" h="8" rounded="full" bg={a.isEnabled ? 'blue.500' : 'gray.400'}
                    display="flex" alignItems="center" justifyContent="center" flexShrink={0}
                  >
                    <Text color="white" fontWeight="bold" fontSize="sm">
                      {a.name[0]?.toUpperCase()}
                    </Text>
                  </Box>
                  <Box flex="1" minW="0">
                    <HStack gap="1">
                      <Text fontSize="sm" fontWeight="medium" truncate>{a.name}</Text>
                      {a.isDefault && <Badge size="xs" colorPalette="yellow">DEFAULT</Badge>}
                    </HStack>
                  </Box>
                  <Box
                    w="2" h="2" rounded="full"
                    bg={a.isEnabled ? 'green.400' : 'gray.300'}
                    flexShrink={0}
                  />
                </HStack>
              )
            }}
          </For>
        </Box>
      </Flex>

      {/* 右侧 */}
      {selected ? (
        <AgentDetail
          key={selected.id}
          agent={selected}
          allAgents={agents}
          onUpdated={handleUpdated}
          onDeleted={handleDeleted}
        />
      ) : (
        <Flex flex="1" align="center" justify="center">
          <VStack gap="2">
            <Bot size={48} opacity={0.2} />
            <Em color="gray.400">从左侧选择一个 Agent</Em>
          </VStack>
        </Flex>
      )}

      <CreateDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        onCreated={load}
      />
    </Flex>
  )
}
