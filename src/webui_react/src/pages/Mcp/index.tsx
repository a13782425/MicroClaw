import React, { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Table, Switch, Dialog,
  createListCollection, Select, Portal,
} from '@chakra-ui/react'
import { Plus, Trash2, Edit, ChevronDown, ChevronRight, Zap, RefreshCw } from 'lucide-react'
import {
  listMcpServers, createMcpServer, updateMcpServer, deleteMcpServer,
  testMcpServer, listMcpServerTools,
  type McpServerConfig, type McpTransportType, type McpToolInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

const TRANSPORT_OPTIONS = [
  { value: 'stdio', label: 'Stdio（本地进程）' },
  { value: 'sse', label: 'SSE（HTTP 流）' },
  { value: 'http', label: 'HTTP' },
]
const transportCollection = createListCollection({ items: TRANSPORT_OPTIONS })

function transportLabel(t: McpTransportType): string {
  return { stdio: 'Stdio', sse: 'SSE', http: 'HTTP' }[t] ?? t
}

type KVEntry = { key: string; value: string }

function kvToRecord(list: KVEntry[]): Record<string, string> {
  return Object.fromEntries(list.filter((e) => e.key.trim()).map((e) => [e.key.trim(), e.value]))
}

function recordToKv(rec: Record<string, string> | null | undefined): KVEntry[] {
  if (!rec) return [{ key: '', value: '' }]
  const entries = Object.entries(rec).map(([k, v]) => ({ key: k, value: v }))
  return entries.length ? entries : [{ key: '', value: '' }]
}

// ─── KV 编辑器 ─────────────────────────────────────────────────────────────────

function KVEditor({ label, entries, onChange }: { label: string; entries: KVEntry[]; onChange: (v: KVEntry[]) => void }) {
  const update = (idx: number, field: 'key' | 'value', val: string) => {
    const copy = entries.map((e, i) => i === idx ? { ...e, [field]: val } : e)
    onChange(copy)
  }
  const remove = (idx: number) => onChange(entries.filter((_, i) => i !== idx))
  const add = () => onChange([...entries, { key: '', value: '' }])

  return (
    <Box>
      <HStack mb="1" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">{label}</Text>
        <Button size="xs" variant="ghost" onClick={add}><Plus size={12} /></Button>
      </HStack>
      <VStack gap="1" align="stretch">
        {entries.map((e, i) => (
          <HStack key={i} gap="1">
            <Input size="xs" placeholder="Key" value={e.key} onChange={(ev) => update(i, 'key', ev.target.value)} />
            <Input size="xs" placeholder="Value" value={e.value} onChange={(ev) => update(i, 'value', ev.target.value)} />
            <Button size="xs" variant="ghost" colorPalette="red" onClick={() => remove(i)}><Trash2 size={10} /></Button>
          </HStack>
        ))}
      </VStack>
    </Box>
  )
}

// ─── 弹窗 ──────────────────────────────────────────────────────────────────────

interface McpDialogProps {
  open: boolean
  editing: McpServerConfig | null
  onClose: () => void
  onSaved: () => void
}

function McpDialog({ open, editing, onClose, onSaved }: McpDialogProps) {
  const [name, setName] = useState('')
  const [transport, setTransport] = useState<McpTransportType>('stdio')
  const [command, setCommand] = useState('')
  const [args, setArgs] = useState('')
  const [env, setEnv] = useState<KVEntry[]>([{ key: '', value: '' }])
  const [url, setUrl] = useState('')
  const [headers, setHeaders] = useState<KVEntry[]>([{ key: '', value: '' }])
  const [saving, setSaving] = useState(false)
  const [testResult, setTestResult] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)

  useEffect(() => {
    if (open) {
      if (editing) {
        setName(editing.name)
        setTransport(editing.transportType)
        setCommand(editing.command ?? '')
        setArgs((editing.args ?? []).join(' '))
        setEnv(recordToKv(editing.env as Record<string, string> | null | undefined))
        setUrl(editing.url ?? '')
        setHeaders(recordToKv(editing.headers))
      } else {
        setName(''); setTransport('stdio'); setCommand(''); setArgs('')
        setEnv([{ key: '', value: '' }]); setUrl(''); setHeaders([{ key: '', value: '' }])
      }
      setTestResult(null)
    }
  }, [open, editing])

  const handleSave = async () => {
    if (!name.trim()) { toaster.create({ type: 'error', title: '请填写名称' }); return }
    setSaving(true)
    try {
      const base = {
        name: name.trim(),
        transportType: transport,
        command: transport === 'stdio' ? command.trim() || undefined : undefined,
        args: transport === 'stdio' ? args.trim().split(/\s+/).filter(Boolean) : undefined,
        env: transport === 'stdio' ? kvToRecord(env) : undefined,
        url: transport !== 'stdio' ? url.trim() || undefined : undefined,
        headers: transport !== 'stdio' ? kvToRecord(headers) : undefined,
      }
      if (editing) {
        await updateMcpServer({ id: editing.id, ...base })
      } else {
        await createMcpServer({ ...base, isEnabled: true })
      }
      toaster.create({ type: 'success', title: editing ? 'MCP 服务器已更新' : 'MCP 服务器已创建' })
      onSaved()
      onClose()
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  const handleTest = async () => {
    if (!editing) return
    setTesting(true)
    setTestResult(null)
    try {
      const res = await testMcpServer(editing.id)
      if (res.success) {
        setTestResult(`✅ 连接成功，发现 ${res.toolCount ?? 0} 个工具：${(res.toolNames ?? []).join(', ')}`)
      } else {
        setTestResult(`❌ 连接失败：${res.error}`)
      }
    } catch {
      setTestResult('❌ 测试请求失败')
    } finally {
      setTesting(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) onClose() }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="540px">
          <Dialog.Header>
            <Dialog.Title>{editing ? '编辑 MCP 服务器' : '新建 MCP 服务器'}</Dialog.Title>
          </Dialog.Header>
          <Dialog.Body>
            <VStack gap="4" align="stretch">
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">名称 <Text as="span" color="red.500">*</Text></Text>
                <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="服务器名称" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">传输类型</Text>
                <Select.Root collection={transportCollection} value={[transport]} onValueChange={(e) => { setTransport(e.value[0] as McpTransportType); setTestResult(null) }}>
                  <Select.HiddenSelect />
                  <Select.Control><Select.Trigger><Select.ValueText /></Select.Trigger><Select.IndicatorGroup><Select.Indicator /></Select.IndicatorGroup></Select.Control>
                  <Portal><Select.Positioner><Select.Content>
                    {TRANSPORT_OPTIONS.map((o) => <Select.Item key={o.value} item={o}>{o.label}</Select.Item>)}
                  </Select.Content></Select.Positioner></Portal>
                </Select.Root>
              </Box>
              {transport === 'stdio' && (
                <>
                  <Box>
                    <Text fontSize="sm" mb="1" fontWeight="medium">命令</Text>
                    <Input value={command} onChange={(e) => setCommand(e.target.value)} placeholder="如 python" />
                  </Box>
                  <Box>
                    <Text fontSize="sm" mb="1" fontWeight="medium">参数（空格分隔）</Text>
                    <Input value={args} onChange={(e) => setArgs(e.target.value)} placeholder="如 server.py --port 8000" />
                  </Box>
                  <KVEditor label="环境变量" entries={env} onChange={setEnv} />
                </>
              )}
              {(transport === 'sse' || transport === 'http') && (
                <>
                  <Box>
                    <Text fontSize="sm" mb="1" fontWeight="medium">URL <Text as="span" color="red.500">*</Text></Text>
                    <Input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="http://localhost:8000/sse" />
                  </Box>
                  <KVEditor label="请求头" entries={headers} onChange={setHeaders} />
                </>
              )}
              {editing && (
                <Box>
                  <Button size="sm" variant="outline" loading={testing} onClick={handleTest}><Zap size={14} />测试连接</Button>
                  {testResult && <Text fontSize="xs" mt="2" color={testResult.startsWith('✅') ? 'green.600' : 'red.500'}>{testResult}</Text>}
                </Box>
              )}
            </VStack>
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>取消</Button>
            <Button colorPalette="blue" loading={saving} onClick={handleSave} disabled={!name.trim()}>保存</Button>
          </Dialog.Footer>
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function McpPage() {
  const [servers, setServers] = useState<McpServerConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [tools, setTools] = useState<Record<string, McpToolInfo[]>>({})
  const [toolsLoading, setToolsLoading] = useState<Record<string, boolean>>({})
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<McpServerConfig | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<McpServerConfig | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listMcpServers()
      setServers(data)
    } catch {
      toaster.create({ type: 'error', title: '加载 MCP 服务器列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const toggleExpand = async (id: string) => {
    const next = !expanded[id]
    setExpanded((prev) => ({ ...prev, [id]: next }))
    if (next && !tools[id]) {
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
  }

  const handleDelete = async (s: McpServerConfig) => {
    try {
      await deleteMcpServer(s.id)
      toaster.create({ type: 'success', title: '已删除' })
      setServers((prev) => prev.filter((x) => x.id !== s.id))
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteTarget(null)
    }
  }

  const openCreate = () => { setEditing(null); setDialogOpen(true) }
  const openEdit = (s: McpServerConfig) => { setEditing(s); setDialogOpen(true) }

  return (
    <Box p="6">
      <HStack mb="4" justify="space-between">
        <Text fontWeight="semibold" fontSize="lg">MCP 服务器</Text>
        <HStack>
          <Button size="sm" variant="outline" onClick={load} loading={loading}><RefreshCw size={14} /></Button>
          <Button size="sm" colorPalette="blue" onClick={openCreate}><Plus size={14} />新建</Button>
        </HStack>
      </HStack>

      {loading && <Box py="8" textAlign="center"><Spinner /></Box>}
      {!loading && servers.length === 0 && (
        <Box py="8" textAlign="center" color="gray.400">暂无 MCP 服务器，点击「新建」添加</Box>
      )}

      {!loading && servers.length > 0 && (
        <Table.Root variant="outline">
          <Table.Header>
            <Table.Row>
              <Table.ColumnHeader w="32px"></Table.ColumnHeader>
              <Table.ColumnHeader>名称</Table.ColumnHeader>
              <Table.ColumnHeader>传输类型</Table.ColumnHeader>
              <Table.ColumnHeader>命令 / URL</Table.ColumnHeader>
              <Table.ColumnHeader>状态</Table.ColumnHeader>
              <Table.ColumnHeader>操作</Table.ColumnHeader>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {servers.map((s) => (
              <React.Fragment key={s.id}>
                <Table.Row>
                  <Table.Cell cursor="pointer" onClick={() => toggleExpand(s.id)}>
                    {expanded[s.id] ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                  </Table.Cell>
                  <Table.Cell fontWeight="medium">{s.name}</Table.Cell>
                  <Table.Cell><Badge size="sm">{transportLabel(s.transportType)}</Badge></Table.Cell>
                  <Table.Cell fontSize="xs" maxW="200px" truncate>
                    {s.transportType === 'stdio' ? (s.command ?? '') : (s.url ?? '')}
                  </Table.Cell>
                  <Table.Cell>
                    <Box w="8px" h="8px" rounded="full" bg={s.isEnabled ? 'green.400' : 'gray.300'} />
                  </Table.Cell>
                  <Table.Cell>
                    <HStack gap="1">
                      <Button size="xs" variant="ghost" onClick={() => openEdit(s)}><Edit size={12} /></Button>
                      <Button size="xs" variant="ghost" colorPalette="red" onClick={() => setDeleteTarget(s)}><Trash2 size={12} /></Button>
                    </HStack>
                  </Table.Cell>
                </Table.Row>
                {expanded[s.id] && (
                  <Table.Row key={`${s.id}-tools`} bg="gray.50" _dark={{ bg: 'gray.900' }}>
                    <Table.Cell colSpan={6} pl="10">
                      {toolsLoading[s.id] && <Spinner size="sm" />}
                      {!toolsLoading[s.id] && (tools[s.id] ?? []).length === 0 && (
                        <Text fontSize="xs" color="gray.400">无工具（或尚未加载）</Text>
                      )}
                      {!toolsLoading[s.id] && (tools[s.id] ?? []).map((t) => (
                        <HStack key={t.name} py="1">
                          <Text fontSize="xs" fontWeight="medium" w="160px">{t.name}</Text>
                          <Text fontSize="xs" color="gray.500">{t.description}</Text>
                        </HStack>
                      ))}
                    </Table.Cell>
                  </Table.Row>
                )}
              </React.Fragment>
            ))}
          </Table.Body>
        </Table.Root>
      )}

      <McpDialog open={dialogOpen} editing={editing} onClose={() => setDialogOpen(false)} onSaved={load} />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除 MCP 服务器"
        description={`确认删除「${deleteTarget?.name}」？`}
        confirmText="删除"
      />
    </Box>
  )
}

