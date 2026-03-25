import { useState, useEffect } from 'react'
import {
  Box, Text, Badge, Tabs, HStack, VStack, Spinner, Button,
  Table, createListCollection, Select, Portal,
} from '@chakra-ui/react'
import { RefreshCw, Wrench } from 'lucide-react'
import {
  listAllTools, listMcpServers, listMcpServerTools, getChannelTools,
  type GlobalToolGroup, type McpServerConfig, type McpToolInfo, type ChannelToolInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'

const CHANNEL_TYPE_OPTIONS = [
  { value: 'feishu', label: '飞书' },
  { value: 'wecom', label: '企业微信' },
  { value: 'wechat', label: '微信' },
  { value: 'web', label: 'Web' },
]
const channelTypeCollection = createListCollection({ items: CHANNEL_TYPE_OPTIONS })

// ─── 内置工具 Tab ──────────────────────────────────────────────────────────────

function BuiltinToolsTab() {
  const [groups, setGroups] = useState<GlobalToolGroup[]>([])
  const [loading, setLoading] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const data = await listAllTools()
      setGroups(data)
    } catch {
      toaster.create({ type: 'error', title: '加载内置工具失败' })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  if (loading) return <Box py="8" textAlign="center"><Spinner /></Box>

  return (
    <Box>
      <HStack mb="4" justify="space-between">
        <Text fontSize="sm" color="gray.500">{groups.length} 个工具分组</Text>
        <Button size="sm" variant="outline" onClick={load}><RefreshCw size={14} />刷新</Button>
      </HStack>
      {groups.length === 0 && <Text color="gray.400" textAlign="center" py="8">暂无内置工具</Text>}
      <VStack gap="4" align="stretch">
        {groups.map((g) => (
          <Box key={g.id} borderWidth="1px" rounded="md" overflow="hidden">
            <HStack px="4" py="3" bg="gray.50" _dark={{ bg: 'gray.800' }}>
              <Wrench size={14} />
              <Text fontWeight="medium" fontSize="sm" flex="1">{g.name}</Text>
              <Badge size="sm" colorPalette={g.type === 'builtin' ? 'orange' : 'blue'}>{g.type === 'builtin' ? '内置' : 'MCP'}</Badge>
              <Text fontSize="xs" color="gray.500">{g.tools.length} 个工具</Text>
              {g.loadError && <Badge size="sm" colorPalette="red">加载错误</Badge>}
            </HStack>
            <Table.Root size="sm">
              <Table.Body>
                {g.tools.map((t) => (
                  <Table.Row key={t.name}>
                    <Table.Cell w="200px" fontWeight="medium" fontSize="xs">{t.name}</Table.Cell>
                    <Table.Cell fontSize="xs" color="gray.500">{t.description}</Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Root>
          </Box>
        ))}
      </VStack>
    </Box>
  )
}

// ─── 渠道工具 Tab ──────────────────────────────────────────────────────────────

function ChannelToolsTab() {
  const [channelType, setChannelType] = useState('feishu')
  const [tools, setTools] = useState<ChannelToolInfo[]>([])
  const [loading, setLoading] = useState(false)

  const load = async (type: string) => {
    setLoading(true)
    try {
      const data = await getChannelTools(type)
      setTools(data)
    } catch {
      toaster.create({ type: 'error', title: '加载渠道工具失败' })
      setTools([])
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load(channelType) }, [channelType]) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <Box>
      <HStack mb="4" gap="3">
        <Text fontSize="sm" fontWeight="medium">渠道类型：</Text>
        <Box w="180px">
          <Select.Root collection={channelTypeCollection} value={[channelType]} onValueChange={(e) => setChannelType(e.value[0])}>
            <Select.HiddenSelect />
            <Select.Control><Select.Trigger><Select.ValueText /></Select.Trigger><Select.IndicatorGroup><Select.Indicator /></Select.IndicatorGroup></Select.Control>
            <Portal><Select.Positioner><Select.Content>
              {CHANNEL_TYPE_OPTIONS.map((o) => <Select.Item key={o.value} item={o}>{o.label}</Select.Item>)}
            </Select.Content></Select.Positioner></Portal>
          </Select.Root>
        </Box>
        <Button size="sm" variant="outline" onClick={() => load(channelType)}><RefreshCw size={14} /></Button>
      </HStack>
      {loading && <Box py="8" textAlign="center"><Spinner /></Box>}
      {!loading && tools.length === 0 && <Text color="gray.400" textAlign="center" py="8">该渠道暂无工具</Text>}
      {!loading && tools.length > 0 && (
        <Table.Root variant="outline">
          <Table.Header>
            <Table.Row>
              <Table.ColumnHeader w="200px">工具名称</Table.ColumnHeader>
              <Table.ColumnHeader>描述</Table.ColumnHeader>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {tools.map((t) => (
              <Table.Row key={t.name}>
                <Table.Cell fontWeight="medium" fontSize="sm">{t.name}</Table.Cell>
                <Table.Cell fontSize="sm" color="gray.500">{t.description}</Table.Cell>
              </Table.Row>
            ))}
          </Table.Body>
        </Table.Root>
      )}
    </Box>
  )
}

// ─── MCP 工具 Tab ──────────────────────────────────────────────────────────────

function McpToolsTab() {
  const [servers, setServers] = useState<McpServerConfig[]>([])
  const [tools, setTools] = useState<Record<string, McpToolInfo[]>>({})
  const [loading, setLoading] = useState(false)
  const [toolsLoading, setToolsLoading] = useState<Record<string, boolean>>({})

  const load = async () => {
    setLoading(true)
    try {
      const data = await listMcpServers()
      setServers(data)
    } catch {
      toaster.create({ type: 'error', title: '加载 MCP 服务器失败' })
    } finally {
      setLoading(false)
    }
  }

  const loadTools = async (id: string) => {
    setToolsLoading((prev) => ({ ...prev, [id]: true }))
    try {
      const t = await listMcpServerTools(id)
      setTools((prev) => ({ ...prev, [id]: t }))
    } catch {
      toaster.create({ type: 'error', title: '加载工具失败' })
    } finally {
      setToolsLoading((prev) => ({ ...prev, [id]: false }))
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  if (loading) return <Box py="8" textAlign="center"><Spinner /></Box>

  return (
    <Box>
      <HStack mb="4" justify="space-between">
        <Text fontSize="sm" color="gray.500">{servers.length} 个 MCP 服务器</Text>
        <Button size="sm" variant="outline" onClick={load}><RefreshCw size={14} />刷新</Button>
      </HStack>
      {servers.length === 0 && <Text color="gray.400" textAlign="center" py="8">暂无 MCP 服务器</Text>}
      <VStack gap="4" align="stretch">
        {servers.map((s) => (
          <Box key={s.id} borderWidth="1px" rounded="md" overflow="hidden">
            <HStack px="4" py="3" bg="gray.50" _dark={{ bg: 'gray.800' }}>
              <Text fontWeight="medium" fontSize="sm" flex="1">{s.name}</Text>
              <Badge size="sm">{s.transportType}</Badge>
              <Box w="8px" h="8px" rounded="full" bg={s.isEnabled ? 'green.400' : 'gray.300'} />
              <Button size="xs" variant="ghost" loading={toolsLoading[s.id]} onClick={() => loadTools(s.id)}>
                <RefreshCw size={12} />加载工具
              </Button>
            </HStack>
            {tools[s.id] && (
              <Table.Root size="sm">
                <Table.Body>
                  {tools[s.id].length === 0 && (
                    <Table.Row><Table.Cell colSpan={2}><Text fontSize="xs" color="gray.400">无工具</Text></Table.Cell></Table.Row>
                  )}
                  {tools[s.id].map((t) => (
                    <Table.Row key={t.name}>
                      <Table.Cell w="200px" fontWeight="medium" fontSize="xs">{t.name}</Table.Cell>
                      <Table.Cell fontSize="xs" color="gray.500">{t.description}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table.Root>
            )}
          </Box>
        ))}
      </VStack>
    </Box>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function ToolsPage() {
  return (
    <Box p="6">
      <Text fontWeight="semibold" fontSize="lg" mb="4">工具总览</Text>
      <Tabs.Root defaultValue="builtin">
        <Tabs.List mb="4">
          <Tabs.Trigger value="builtin">内置工具</Tabs.Trigger>
          <Tabs.Trigger value="channel">渠道工具</Tabs.Trigger>
          <Tabs.Trigger value="mcp">MCP 工具</Tabs.Trigger>
        </Tabs.List>
        <Tabs.Content value="builtin"><BuiltinToolsTab /></Tabs.Content>
        <Tabs.Content value="channel"><ChannelToolsTab /></Tabs.Content>
        <Tabs.Content value="mcp"><McpToolsTab /></Tabs.Content>
      </Tabs.Root>
    </Box>
  )
}

