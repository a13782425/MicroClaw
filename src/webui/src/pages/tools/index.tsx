import { useState, useEffect, useMemo } from 'react'
import {
  Box, Text, Badge, Tabs, HStack, VStack, Spinner, Button, Table,
} from '@chakra-ui/react'
import { RefreshCw, Wrench } from 'lucide-react'
import { listAllTools, type GlobalToolGroup } from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'

// ─── 分组卡片 ──────────────────────────────────────────────────────────────────

const TYPE_LABELS: Record<string, { label: string; color: string }> = {
  builtin: { label: '内置', color: 'orange' },
  channel: { label: '渠道', color: 'teal' },
  mcp:     { label: 'MCP', color: 'blue' },
}

function ToolGroupCard({ group }: { group: GlobalToolGroup }) {
  const badge = TYPE_LABELS[group.type] ?? { label: group.type, color: 'gray' }
  return (
    <Box borderWidth="1px" rounded="md" overflow="hidden">
      <HStack px="4" py="3" bg="var(--mc-surface-muted)">
        <Wrench size={14} />
        <Text fontWeight="medium" fontSize="sm" flex="1">{group.name}</Text>
        <Badge size="sm" colorPalette={badge.color}>{badge.label}</Badge>
        <Text fontSize="xs" color="var(--mc-text-muted)">{group.tools.length} 个工具</Text>
        {group.loadError && <Badge size="sm" colorPalette="red">加载错误</Badge>}
      </HStack>
      <Table.Root size="sm">
        <Table.Body>
          {group.tools.map((t) => (
            <Table.Row key={t.name}>
              <Table.Cell w="200px" fontWeight="medium" fontSize="xs">{t.name}</Table.Cell>
              <Table.Cell fontSize="xs" color="var(--mc-text-muted)">{t.description}</Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table.Root>
    </Box>
  )
}

// ─── 工具列表（按类型过滤）──────────────────────────────────────────────────────

function ToolGroupList({ groups, type }: { groups: GlobalToolGroup[]; type: string }) {
  const filtered = useMemo(
    () => groups.filter((g) => g.type === type),
    [groups, type],
  )

  if (filtered.length === 0) {
    return <Text color="var(--mc-text-muted)" textAlign="center" py="8">暂无工具</Text>
  }

  return (
    <VStack gap="4" align="stretch">
      <Text fontSize="sm" color="var(--mc-text-muted)">{filtered.length} 个工具分组</Text>
      {filtered.map((g) => <ToolGroupCard key={g.id} group={g} />)}
    </VStack>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function ToolsPage() {
  const [groups, setGroups] = useState<GlobalToolGroup[]>([])
  const [loading, setLoading] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const data = await listAllTools()
      setGroups(data)
    } catch {
      toaster.create({ type: 'error', title: '加载工具失败' })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const counts = useMemo(() => ({
    builtin: groups.filter((g) => g.type === 'builtin').length,
    channel: groups.filter((g) => g.type === 'channel').length,
    mcp:     groups.filter((g) => g.type === 'mcp').length,
  }), [groups])

  return (
    <Box p="6">
      <HStack mb="4" justify="space-between">
        <Text fontWeight="semibold" fontSize="lg">工具总览</Text>
        <Button size="sm" variant="outline" onClick={load} loading={loading}><RefreshCw size={14} />刷新</Button>
      </HStack>
      {loading
        ? <Box py="8" textAlign="center"><Spinner /></Box>
        : (
          <Tabs.Root defaultValue="builtin">
            <Tabs.List mb="4">
              <Tabs.Trigger value="builtin">内置工具 ({counts.builtin})</Tabs.Trigger>
              <Tabs.Trigger value="channel">渠道工具 ({counts.channel})</Tabs.Trigger>
              <Tabs.Trigger value="mcp">MCP 工具 ({counts.mcp})</Tabs.Trigger>
            </Tabs.List>
            <Tabs.Content value="builtin"><ToolGroupList groups={groups} type="builtin" /></Tabs.Content>
            <Tabs.Content value="channel"><ToolGroupList groups={groups} type="channel" /></Tabs.Content>
            <Tabs.Content value="mcp"><ToolGroupList groups={groups} type="mcp" /></Tabs.Content>
          </Tabs.Root>
        )}
    </Box>
  )
}

