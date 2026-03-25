import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Tabs, For, Em, Switch, createListCollection,
  Select,
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
  type AgentConfig, type AgentCreateRequest, type ToolGroup,
  type ToolGroupConfig, type SkillConfig, type McpServerConfig,
  type AgentDnaFileInfo,
} from '@/api/gateway'

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
  const [enabledIds, setEnabledIds] = useState<string[]>(agent.enabledMcpServerIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify(enabledIds.sort()) !== JSON.stringify([...agent.enabledMcpServerIds].sort())

  useEffect(() => {
    setEnabledIds(agent.enabledMcpServerIds)
  }, [agent.id, agent.enabledMcpServerIds])

  useEffect(() => {
    setLoading(true)
    listMcpServers()
      .then(setServers)
      .catch(() => toaster.create({ type: 'error', title: '加载 MCP Servers 失败' }))
      .finally(() => setLoading(false))
  }, [])

  const toggle = (id: string, val: boolean) => {
    setEnabledIds((prev) => val ? [...prev, id] : prev.filter((x) => x !== id))
  }

  const save = async () => {
    setSaving(true)
    try {
      const res = await updateAgent({ id: agent.id, enabledMcpServerIds: enabledIds })
      onUpdated({ ...agent, enabledMcpServerIds: enabledIds })
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
        <Badge size="sm" colorPalette="blue">{enabledIds.length} 个已启用</Badge>
      </HStack>
      {servers.length === 0 && (
        <Text color="gray.500" fontSize="sm">暂无全局 MCP Server，请先在 MCP 管理页创建</Text>
      )}
      <VStack gap="2" align="stretch">
        {servers.map((srv) => (
          <HStack key={srv.id} px="3" py="2" borderWidth="1px" rounded="md">
            <Switch.Root
              size="sm"
              checked={enabledIds.includes(srv.id)}
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
  const [boundIds, setBoundIds] = useState<string[]>(agent.boundSkillIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify(boundIds.sort()) !== JSON.stringify([...agent.boundSkillIds].sort())

  useEffect(() => { setBoundIds(agent.boundSkillIds) }, [agent.id, agent.boundSkillIds])

  useEffect(() => {
    setLoading(true)
    listSkills()
      .then(setSkills)
      .catch(() => toaster.create({ type: 'error', title: '加载技能失败' }))
      .finally(() => setLoading(false))
  }, [])

  const toggle = (id: string, val: boolean) => {
    setBoundIds((prev) => val ? [...prev, id] : prev.filter((x) => x !== id))
  }

  const save = async () => {
    setSaving(true)
    try {
      await updateAgent({ id: agent.id, boundSkillIds: boundIds })
      onUpdated({ ...agent, boundSkillIds: boundIds })
      toaster.create({ type: 'success', title: '技能绑定已保存' })
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
            保存技能绑定
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
              checked={boundIds.includes(sk.id)}
              onChange={(e) => toggle(sk.id, e.target.checked)}
              style={{ width: 16, height: 16, cursor: 'pointer' }}
            />
            <Box flex="1">
              <HStack>
                <Text fontSize="sm" fontWeight="medium">{sk.name}</Text>
                <Badge size="xs" colorPalette="purple">{sk.skillType}</Badge>
              </HStack>
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

// ─────────────────── 右侧详情 ─────────────────────────────────────────────────

function AgentDetail({
  agent,
  onUpdated,
  onDeleted,
}: {
  agent: AgentConfig
  onUpdated: (a: AgentConfig) => void
  onDeleted: (id: string) => void
}) {
  const [deleting, setDeleting] = useState(false)
  const [togglingEnabled, setTogglingEnabled] = useState(false)
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
          <Tabs.Trigger value="tools">工具</Tabs.Trigger>
          <Tabs.Trigger value="mcp">MCP</Tabs.Trigger>
          <Tabs.Trigger value="skills">技能</Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="overview" flex="1" overflowY="auto">
          <Box p="4">
            <HStack gap="4" mb="4" flexWrap="wrap">
              <VStack gap="0" align="start" bg="gray.50" _dark={{ bg: 'gray.800' }} p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="gray.500">MCP Server</Text>
                <Text fontWeight="semibold">{agent.enabledMcpServerIds.length} 个</Text>
              </VStack>
              <VStack gap="0" align="start" bg="gray.50" _dark={{ bg: 'gray.800' }} p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="gray.500">绑定技能</Text>
                <Text fontWeight="semibold">{agent.boundSkillIds.length} 个</Text>
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
          </Box>
        </Tabs.Content>

        <Tabs.Content value="tools" flex="1" overflowY="auto" p="0">
          <ToolsTab agent={agent} />
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
