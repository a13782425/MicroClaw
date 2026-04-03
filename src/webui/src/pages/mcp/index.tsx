import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Card, Dialog, Alert,
  createListCollection, Select, Portal, Tooltip,
} from '@chakra-ui/react'
import { Plus, Trash2, Zap, RefreshCw, Puzzle, CheckCircle, XCircle } from 'lucide-react'
import {
  listMcpServers, createMcpServer, updateMcpServer, deleteMcpServer,
  testMcpServer, listMcpServerTools,
  type McpServerConfig, type McpTransportType, type McpToolInfo, type McpEnvVarInfo,
} from '@/api/gateway'
import { McpServerCard } from './mcp-cards'
import { getApiErrorMessage } from '@/api/request'
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

function isSensitiveKey(key: string): boolean {
  return /authorization|api[-_]?key|token|secret|password/i.test(key)
}

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
            <Input
              size="xs"
              type={isSensitiveKey(e.key) ? 'password' : 'text'}
              placeholder={isSensitiveKey(e.key) ? '直接值、Bearer ${TOKEN} 或 ${TOKEN}' : '直接值或 ${ENV_VAR}'}
              value={e.value}
              onChange={(ev) => update(i, 'value', ev.target.value)}
            />
            <Button size="xs" variant="ghost" colorPalette="red" onClick={() => remove(i)}><Trash2 size={10} /></Button>
          </HStack>
        ))}
      </VStack>
    </Box>
  )
}

// ─── 所需环境变量展示区 ──────────────────────────────────────────────────────────

function RequiredEnvVarsList({ vars }: { vars: McpEnvVarInfo[] }) {
  if (vars.length === 0) return null
  const hasMissing = vars.some((v) => !v.isSet)
  return (
    <Alert.Root status={hasMissing ? 'warning' : 'info'} size="sm" borderRadius="md">
      <Alert.Indicator />
      <Alert.Content>
        <Alert.Title fontSize="xs" mb="1">所需环境变量</Alert.Title>
        <VStack gap="1" align="stretch">
          {vars.map((v) => (
            <HStack key={v.name} gap="2" fontSize="xs">
              {v.isSet
                ? <CheckCircle size={12} color="var(--chakra-colors-green-500)" />
                : <XCircle size={12} color="var(--chakra-colors-red-500)" />}
              <Text fontFamily="mono" fontWeight="medium">${`{${v.name}}`}</Text>
              <Badge size="xs" variant="subtle" colorPalette="gray">{v.foundIn}</Badge>
              <Text color={v.isSet ? 'green.600' : 'red.500'}>
                {v.isSet ? '已设置' : '未设置'}
              </Text>
            </HStack>
          ))}
        </VStack>
      </Alert.Content>
    </Alert.Root>
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
  const isPlugin = editing?.source === 'plugin'
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
    if (!name.trim() && !isPlugin) { toaster.create({ type: 'error', title: '请填写名称' }); return }
    setSaving(true)
    try {
      if (editing && isPlugin) {
        // 插件 MCP 只允许更新 Env 和 Headers
        await updateMcpServer({
          id: editing.id,
          env: kvToRecord(env),
          headers: kvToRecord(headers),
        })
      } else if (editing) {
        const base = {
          name: name.trim(),
          transportType: transport,
          command: transport === 'stdio' ? command.trim() || undefined : undefined,
          args: transport === 'stdio' ? args.trim().split(/\s+/).filter(Boolean) : undefined,
          env: transport === 'stdio' ? kvToRecord(env) : undefined,
          url: transport !== 'stdio' ? url.trim() || undefined : undefined,
          headers: transport !== 'stdio' ? kvToRecord(headers) : undefined,
        }
        await updateMcpServer({ id: editing.id, ...base })
      } else {
        const base = {
          name: name.trim(),
          transportType: transport,
          command: transport === 'stdio' ? command.trim() || undefined : undefined,
          args: transport === 'stdio' ? args.trim().split(/\s+/).filter(Boolean) : undefined,
          env: transport === 'stdio' ? kvToRecord(env) : undefined,
          url: transport !== 'stdio' ? url.trim() || undefined : undefined,
          headers: transport !== 'stdio' ? kvToRecord(headers) : undefined,
        }
        await createMcpServer({ ...base, isEnabled: true })
      }
      toaster.create({ type: 'success', title: editing ? 'MCP 服务器已更新' : 'MCP 服务器已创建' })
      onSaved()
      onClose()
    } catch (error) {
      toaster.create({
        type: 'error',
        title: '保存失败',
        description: getApiErrorMessage(error, '保存 MCP 服务器失败'),
      })
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
    } catch (error) {
      setTestResult(`❌ ${getApiErrorMessage(error, '测试请求失败')}`)
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
            <Dialog.Title>
              {editing
                ? (isPlugin ? `查看插件 MCP：${editing.name}` : '编辑 MCP 服务器')
                : '新建 MCP 服务器'}
            </Dialog.Title>
          </Dialog.Header>
          <Dialog.Body>
            <VStack gap="4" align="stretch">
              {/* 插件来源提示 */}
              {isPlugin && editing && (
                <Alert.Root status="info" size="sm" borderRadius="md">
                  <Alert.Indicator><Puzzle size={12} /></Alert.Indicator>
                  <Alert.Content>
                    <Alert.Description fontSize="xs">
                      此 MCP 服务器来自插件「{editing.pluginName}」，只能修改环境变量和请求头字段，并不允许手动删除。
                    </Alert.Description>
                  </Alert.Content>
                </Alert.Root>
              )}

              {/* 所需环境变量展示 */}
              {isPlugin && editing && (editing.requiredEnvVars ?? []).length > 0 && (
                <RequiredEnvVarsList vars={editing.requiredEnvVars!} />
              )}

              {!isPlugin && (
                <Box>
                  <Text fontSize="sm" mb="1" fontWeight="medium">名称 <Text as="span" color="red.500">*</Text></Text>
                  <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="服务器名称" />
                </Box>
              )}
              {!isPlugin && (
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
              )}

              {/* 插件 MCP 只读展示基本信息 */}
              {isPlugin && editing && (
                <VStack gap="2" align="stretch">
                  <HStack><Text fontSize="sm" color="var(--mc-text-muted)" w="80px">传输类型</Text><Badge size="sm">{transportLabel(editing.transportType)}</Badge></HStack>
                  {editing.transportType === 'stdio' && (
                    <>
                      {editing.command && <HStack align="start"><Text fontSize="sm" color="var(--mc-text-muted)" w="80px">命令</Text><Text fontSize="sm" fontFamily="mono">{editing.command}</Text></HStack>}
                      {(editing.args ?? []).length > 0 && <HStack align="start"><Text fontSize="sm" color="var(--mc-text-muted)" w="80px">参数</Text><Text fontSize="sm" fontFamily="mono">{(editing.args ?? []).join(' ')}</Text></HStack>}
                    </>
                  )}
                  {editing.transportType !== 'stdio' && editing.url && (
                    <HStack align="start"><Text fontSize="sm" color="var(--mc-text-muted)" w="80px">URL</Text><Text fontSize="sm" fontFamily="mono" wordBreak="break-all">{editing.url}</Text></HStack>
                  )}
                </VStack>
              )}

              {/* Stdio 模式输入字段 */}
              {!isPlugin && transport === 'stdio' && (
                <>
                  <Box>
                    <Text fontSize="sm" mb="1" fontWeight="medium">命令</Text>
                    <Input value={command} onChange={(e) => setCommand(e.target.value)} placeholder="如 python" />
                  </Box>
                  <Box>
                    <Text fontSize="sm" mb="1" fontWeight="medium">参数（空格分隔）</Text>
                    <Input value={args} onChange={(e) => setArgs(e.target.value)} placeholder="如 server.py --port 8000" />
                  </Box>
                </>
              )}
              {!isPlugin && (transport === 'sse' || transport === 'http') && (
                <Box>
                  <Text fontSize="sm" mb="1" fontWeight="medium">URL <Text as="span" color="red.500">*</Text></Text>
                  <Input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="http://localhost:8000/sse" />
                  <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">URL 支持直接输入，或使用 {"${ENV_VAR}"} 拼接运行时环境变量。</Text>
                </Box>
              )}

              {/* 环境变量编辑 */}
              {(transport === 'stdio' || (isPlugin && editing?.transportType === 'stdio')) && (
                <>
                  {isPlugin && <Text fontSize="xs" color="var(--mc-link-color)" fontWeight="medium">覆盖插件默认环境变量（空则保持插件原始默认值）</Text>}
                  <KVEditor label="环境变量" entries={env} onChange={setEnv} />
                  <Text fontSize="xs" color="var(--mc-text-muted)" mt="-2">支持直接输入值，或使用 {"${ENV_VAR}"} 引用当前服务进程环境变量。</Text>
                </>
              )}

              {/* 请求头编辑 */}
              {(transport === 'sse' || transport === 'http' || (isPlugin && editing?.transportType !== 'stdio')) && (
                <>
                  {isPlugin && <Text fontSize="xs" color="var(--mc-link-color)" fontWeight="medium">覆盖插件默认请求头（空则保持插件原始默认值）</Text>}
                  <KVEditor label="请求头" entries={headers} onChange={setHeaders} />
                  <Text fontSize="xs" color="var(--mc-text-muted)" mt="-2">请求头支持直接值，也支持例如 Bearer {"${TOKEN}"} 这样的环境变量引用。</Text>
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
            <Button colorPalette="blue" loading={saving} onClick={handleSave}
              disabled={!isPlugin && !name.trim()}>保存</Button>
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
    } catch (error) {
      toaster.create({
        type: 'error',
        title: '加载 MCP 服务器列表失败',
        description: getApiErrorMessage(error, '加载 MCP 服务器列表失败'),
      })
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
      } catch (error) {
        toaster.create({
          type: 'error',
          title: '加载工具失败',
          description: getApiErrorMessage(error, '加载工具失败'),
        })
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
    } catch (error) {
      toaster.create({
        type: 'error',
        title: '删除失败',
        description: getApiErrorMessage(error, '删除 MCP 服务器失败'),
      })
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
        <Box py="8" textAlign="center" color="var(--mc-text-muted)">暂无 MCP 服务器，点击「新建」添加</Box>
      )}

      {!loading && servers.length > 0 && (
        <VStack gap="3" align="stretch">
          {servers.map((s) => (
            <McpServerCard
              key={s.id}
              server={s}
              expanded={!!expanded[s.id]}
              tools={tools[s.id]}
              toolsLoading={!!toolsLoading[s.id]}
              onExpand={toggleExpand}
              onEdit={openEdit}
              onDelete={setDeleteTarget}
            />
          ))}
        </VStack>
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

