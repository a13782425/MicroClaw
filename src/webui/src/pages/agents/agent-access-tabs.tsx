import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Textarea, Switch,
} from '@chakra-ui/react'
import { toaster } from '@/components/ui/toaster'
import {
  listAgentTools,
  updateAgentToolSettings,
  listMcpServers,
  updateAgent,
  listSkills,
  listSubAgents,
  listAgentDna,
  updateAgentDna,
  type AgentConfig,
  type ToolGroup,
  type ToolGroupConfig,
  type SkillConfig,
  type McpServerConfig,
  type AgentDnaFileInfo,
  type SubAgentInfo,
} from '@/api/gateway'

export function ToolsTab({ agent }: { agent: AgentConfig }) {
  const [groups, setGroups] = useState<ToolGroup[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [dirty, setDirty] = useState(false)
  const localRef = useRef<ToolGroup[]>([])

  const load = async () => {
    setLoading(true)
    try {
      const result = await listAgentTools(agent.id)
      const copy = JSON.parse(JSON.stringify(result.groups)) as ToolGroup[]
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

  const toggleGroup = (groupId: string, value: boolean) => {
    setGroups((prev) => prev.map((group) => group.id === groupId ? { ...group, isEnabled: value } : group))
    setDirty(true)
  }

  const toggleTool = (groupId: string, toolName: string, value: boolean) => {
    setGroups((prev) => prev.map((group) =>
      group.id === groupId
        ? { ...group, tools: group.tools.map((tool) => tool.name === toolName ? { ...tool, isEnabled: value } : tool) }
        : group,
    ))
    setDirty(true)
  }

  const save = async () => {
    setSaving(true)
    try {
      const configs: ToolGroupConfig[] = groups.map((group) => ({
        groupId: group.id,
        isEnabled: group.isEnabled,
        disabledToolNames: group.tools.filter((tool) => !tool.isEnabled).map((tool) => tool.name),
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
          <Button size="sm" variant="outline" data-mc-refresh="true" onClick={load} loading={loading}>刷新</Button>
        </HStack>
      </HStack>
      {groups.length === 0 && (
        <Text color="var(--mc-text-muted)" fontSize="sm">点击「刷新」加载工具列表</Text>
      )}
      <VStack gap="2" align="stretch">
        {groups.map((group) => (
          <Box key={group.id} borderWidth="1px" rounded="md" overflow="hidden">
            <HStack px="3" py="2" bg="var(--mc-surface-muted)">
              <Switch.Root size="sm" checked={group.isEnabled} onCheckedChange={(e) => toggleGroup(group.id, e.checked)}>
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
              </Switch.Root>
              <Text fontSize="sm" fontWeight="medium" flex="1">{group.name}</Text>
              <Badge size="xs" colorPalette={group.type === 'builtin' ? 'orange' : 'blue'}>
                {group.type === 'builtin' ? '内置' : 'MCP'}
              </Badge>
              <Text fontSize="xs" color="var(--mc-text-muted)">{group.tools.length} 个工具</Text>
            </HStack>
            <VStack gap="0" divideY="1px" align="stretch" px="3">
              {group.tools.map((tool) => (
                <HStack key={tool.name} py="2">
                  <Switch.Root
                    size="sm"
                    checked={tool.isEnabled}
                    disabled={!group.isEnabled}
                    onCheckedChange={(e) => toggleTool(group.id, tool.name, e.checked)}
                  >
                    <Switch.HiddenInput />
                    <Switch.Control><Switch.Thumb /></Switch.Control>
                  </Switch.Root>
                  <Box flex="1">
                    <Text fontSize="xs" fontWeight="medium">{tool.name}</Text>
                    {tool.description && <Text fontSize="xs" color="var(--mc-text-muted)" truncate>{tool.description}</Text>}
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

export function McpTab({ agent, onUpdated }: { agent: AgentConfig; onUpdated: (agent: AgentConfig) => void }) {
  const [servers, setServers] = useState<McpServerConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [disabledIds, setDisabledIds] = useState<string[]>(agent.disabledMcpServerIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify([...disabledIds].sort()) !== JSON.stringify([...agent.disabledMcpServerIds].sort())

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
    setDisabledIds((prev) => enabled ? prev.filter((value) => value !== id) : [...prev, id])
  }

  const save = async () => {
    setSaving(true)
    try {
      await updateAgent({ id: agent.id, disabledMcpServerIds: disabledIds })
      onUpdated({ ...agent, disabledMcpServerIds: disabledIds })
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
        <Text color="var(--mc-text-muted)" fontSize="sm">暂无全局 MCP Server，请先在 MCP 管理页创建</Text>
      )}
      <VStack gap="2" align="stretch">
        {servers.map((server) => (
          <HStack key={server.id} px="3" py="2" borderWidth="1px" rounded="md">
            <Switch.Root size="sm" checked={!disabledIds.includes(server.id)} onCheckedChange={(e) => toggle(server.id, e.checked)}>
              <Switch.HiddenInput />
              <Switch.Control><Switch.Thumb /></Switch.Control>
            </Switch.Root>
            <Badge size="xs" colorPalette="gray">{server.transportType}</Badge>
            <Text fontSize="sm" flex="1">{server.name}</Text>
            <Text fontSize="xs" color="var(--mc-text-muted)" truncate maxW="200px">
              {server.transportType === 'stdio'
                ? [server.command, ...(server.args ?? [])].join(' ')
                : server.url}
            </Text>
            {!server.isEnabled && <Badge size="xs" colorPalette="orange">全局已禁用</Badge>}
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

export function SkillsTab({ agent, onUpdated }: { agent: AgentConfig; onUpdated: (agent: AgentConfig) => void }) {
  const [skills, setSkills] = useState<SkillConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [disabledIds, setDisabledIds] = useState<string[]>(agent.disabledSkillIds)
  const [saving, setSaving] = useState(false)
  const isDirty = JSON.stringify([...disabledIds].sort()) !== JSON.stringify([...agent.disabledSkillIds].sort())

  useEffect(() => { setDisabledIds(agent.disabledSkillIds) }, [agent.id, agent.disabledSkillIds])

  useEffect(() => {
    setLoading(true)
    listSkills()
      .then(setSkills)
      .catch(() => toaster.create({ type: 'error', title: '加载技能失败' }))
      .finally(() => setLoading(false))
  }, [])

  const toggle = (id: string, enabled: boolean) => {
    setDisabledIds((prev) => enabled ? prev.filter((value) => value !== id) : [...prev, id])
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
        <Text color="var(--mc-text-muted)" fontSize="sm">暂无可用技能</Text>
      )}
      <VStack gap="2" align="stretch">
        {skills.map((skill) => (
          <HStack key={skill.id} px="3" py="2" borderWidth="1px" rounded="md">
            <input
              type="checkbox"
              checked={!disabledIds.includes(skill.id)}
              onChange={(e) => toggle(skill.id, e.target.checked)}
              style={{ width: 16, height: 16, cursor: 'pointer' }}
            />
            <Box flex="1">
              <Text fontSize="sm" fontWeight="medium">{skill.name}</Text>
              {skill.description && <Text fontSize="xs" color="var(--mc-text-muted)">{skill.description}</Text>}
            </Box>
          </HStack>
        ))}
      </VStack>
    </Box>
  )
}

export function DnaTab({ agent }: { agent: AgentConfig }) {
  const [files, setFiles] = useState<AgentDnaFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [activeFile, setActiveFile] = useState<string | null>(null)

  useEffect(() => {
    setActiveFile(null)
  }, [agent.id])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listAgentDna(agent.id)
      setFiles(data)
      const init: Record<string, string> = {}
      data.forEach((file) => { init[file.fileName] = file.content })
      setEdits(init)
      if (data.length > 0) {
        const nextActiveFile = activeFile && data.some((file) => file.fileName === activeFile)
          ? activeFile
          : data[0].fileName
        setActiveFile(nextActiveFile)
      } else {
        setActiveFile(null)
      }
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
  if (files.length === 0) return <Box p="4"><Text color="var(--mc-text-muted)" fontSize="sm">暂无 DNA 文件</Text></Box>

  const currentFile = files.find((file) => file.fileName === activeFile)

  return (
    <Flex h="100%" direction="column">
      <HStack gap="1" px="3" pt="3" flexWrap="wrap">
        {files.map((file) => (
          <Button
            key={file.fileName}
            size="xs"
            variant={activeFile === file.fileName ? 'solid' : 'outline'}
            colorPalette="blue"
            onClick={() => setActiveFile(file.fileName)}
          >
            {file.fileName.replace('.md', '')}
          </Button>
        ))}
      </HStack>

      {currentFile && (
        <Flex direction="column" flex="1" p="3" gap="2" overflow="hidden">
          {currentFile.description && (
            <Text fontSize="xs" color="var(--mc-text-muted)">{currentFile.description}</Text>
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
            <Button size="sm" variant="outline" data-mc-refresh="true" onClick={load} loading={loading}>刷新</Button>
          </HStack>
        </Flex>
      )}
    </Flex>
  )
}

type SubAgentMode = 'all' | 'none' | 'select'

export function SubAgentsTab({ agent, allAgents, onUpdated }: { agent: AgentConfig; allAgents: AgentConfig[]; onUpdated: (agent: AgentConfig) => void }) {
  const [mode, setMode] = useState<SubAgentMode>(
    agent.allowedSubAgentIds === null ? 'all' : agent.allowedSubAgentIds.length === 0 ? 'none' : 'select',
  )
  const [selectedIds, setSelectedIds] = useState<string[]>(agent.allowedSubAgentIds ?? [])
  const [available, setAvailable] = useState<SubAgentInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  const candidates = allAgents.filter((candidate) => candidate.id !== agent.id && candidate.isEnabled)

  useEffect(() => {
    setMode(agent.allowedSubAgentIds === null ? 'all' : agent.allowedSubAgentIds.length === 0 ? 'none' : 'select')
    setSelectedIds(agent.allowedSubAgentIds ?? [])
  }, [agent.id, agent.allowedSubAgentIds])

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
    if (currentValue === null) return true
    return JSON.stringify([...selectedIds].sort()) !== JSON.stringify([...currentValue].sort())
  })()

  const toggle = (id: string, checked: boolean) => {
    setSelectedIds((prev) => checked ? [...prev, id] : prev.filter((value) => value !== id))
  }

  const save = async () => {
    setSaving(true)
    try {
      const allowedSubAgentIds: string[] | null = mode === 'all' ? null : mode === 'none' ? [] : selectedIds
      await updateAgent({ id: agent.id, allowedSubAgentIds, hasAllowedSubAgentIds: true })
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
        {(['all', 'none', 'select'] as const).map((item) => (
          <HStack
            key={item}
            px="3"
            py="2"
            borderWidth="1px"
            rounded="md"
            cursor="pointer"
            bg={mode === item ? 'blue.50' : undefined}
           
            onClick={() => setMode(item)}
          >
            <input type="radio" checked={mode === item} readOnly style={{ cursor: 'pointer' }} />
            <Text fontSize="sm">
              {item === 'all' ? '允许调用所有子代理（默认）' : item === 'none' ? '禁止调用任何子代理' : '仅允许调用指定子代理'}
            </Text>
          </HStack>
        ))}
      </VStack>

      {mode === 'select' && (
        <Box>
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">选择允许调用的子代理：</Text>
          {candidates.length === 0 ? (
            <Text fontSize="sm" color="var(--mc-text-muted)">暂无其他已启用代理</Text>
          ) : (
            <VStack gap="1" align="stretch">
              {candidates.map((candidate) => (
                <HStack key={candidate.id} px="3" py="2" borderWidth="1px" rounded="md">
                  <input
                    type="checkbox"
                    checked={selectedIds.includes(candidate.id)}
                    onChange={(e) => toggle(candidate.id, e.target.checked)}
                    style={{ width: 16, height: 16, cursor: 'pointer' }}
                  />
                  <Box flex="1">
                    <HStack gap="1">
                      <Text fontSize="sm" fontWeight="medium">{candidate.name}</Text>
                      {candidate.isDefault && <Badge size="xs" colorPalette="yellow">DEFAULT</Badge>}
                    </HStack>
                    {candidate.description && <Text fontSize="xs" color="var(--mc-text-muted)">{candidate.description}</Text>}
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
          <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">当前实际可调用的子代理 ({available.length} 个)</Text>
          {available.length === 0 ? (
            <Text fontSize="sm" color="var(--mc-text-muted)">无可调用子代理</Text>
          ) : (
            <HStack gap="1" flexWrap="wrap">
              {available.map((subAgent) => (
                <Badge key={subAgent.id} size="sm" colorPalette="blue">{subAgent.name}</Badge>
              ))}
            </HStack>
          )}
        </Box>
      )}
    </Box>
  )
}
