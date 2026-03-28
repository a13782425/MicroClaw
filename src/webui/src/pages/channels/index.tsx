import { useEffect, useState, useCallback } from 'react'
import {
  Box, Flex, Text, Button, Badge, Switch, Spinner,
  Input, Textarea, Select, Portal, createListCollection, Card,
} from '@chakra-ui/react'
import { Plus, Trash2, Edit, RefreshCw, Send, Radio } from 'lucide-react'
import {
  getChannelTypes, listChannels, createChannel, updateChannel, deleteChannel,
  testChannel, publishChannelMessage, getChannelHealth, getChannelStats,
  listSessions,
  type ChannelConfig, type ChannelTypeInfo, type ChannelCreateRequest, type ChannelUpdateRequest,
  type ChannelHealth, type ChannelStats, type SessionInfo, type ChannelType,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

// ─── 渠道类型图标颜色 ─────────────────────────────────────────────────────────
const TYPE_COLORS: Record<string, string> = {
  feishu: 'cyan', wecom: 'green', wechat: 'teal', web: 'blue',
}
const TYPE_LABELS: Record<string, string> = {
  feishu: '飞书', wecom: '企业微信', wechat: '微信', web: 'Web',
}

// ─── 解析渠道 settings JSON ───────────────────────────────────────────────────
function parseSettings(settings: string): Record<string, string> {
  try { return JSON.parse(settings) ?? {} } catch { return {} }
}

// ─── 渠道类型特有字段配置 ──────────────────────────────────────────────────────
const CONNECTION_MODE_OPTIONS = [
  { value: 'websocket', label: 'WebSocket' },
  { value: 'webhook', label: 'Webhook' },
]
const connectionModeCollection = createListCollection({ items: CONNECTION_MODE_OPTIONS })

const CHANNEL_FIELDS: Record<ChannelType, { key: string; label: string; type?: string; required?: boolean; select?: true }[]> = {
  feishu: [
    { key: 'appId', label: 'App ID', required: true },
    { key: 'appSecret', label: 'App Secret', type: 'password', required: true },
    { key: 'encryptKey', label: 'Encrypt Key' },
    { key: 'verificationToken', label: 'Verification Token' },
    { key: 'connectionMode', label: '连接方式', select: true },
    { key: 'apiBaseUrl', label: 'API Base URL（可选）' },
  ],
  wecom: [
    { key: 'corpId', label: 'Corp ID' },
    { key: 'agentId', label: 'Agent ID' },
    { key: 'secret', label: 'Secret', type: 'password' },
  ],
  wechat: [
    { key: 'appId', label: 'App ID' },
    { key: 'appSecret', label: 'App Secret', type: 'password' },
    { key: 'token', label: 'Token' },
    { key: 'encodingAESKey', label: 'Encoding AES Key（可选）' },
  ],
  web: [
    { key: 'description', label: '描述' },
    { key: 'allowedOrigins', label: '允许来源（逗号分隔）' },
  ],
}

// ─── 编辑/新建弹窗 ────────────────────────────────────────────────────────────
interface ChannelDialogProps {
  open: boolean
  channelType: ChannelType
  editing: ChannelConfig | null
  onClose: () => void
  onSaved: () => void
}

function ChannelDialog({ open, channelType, editing, onClose, onSaved }: ChannelDialogProps) {
  const [displayName, setDisplayName] = useState('')
  const [isEnabled, setIsEnabled] = useState(true)
  const [settings, setSettings] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (open) {
      if (editing) {
        setDisplayName(editing.displayName)
        setIsEnabled(editing.isEnabled)
        setSettings(parseSettings(editing.settings))
      } else {
        setDisplayName('')
        setIsEnabled(true)
        setSettings(channelType === 'feishu' ? { connectionMode: 'websocket' } : {})
      }
    }
  }, [open, editing, channelType])

  if (!open) return null

  const fields = CHANNEL_FIELDS[channelType] ?? []

  const handleSave = async () => {
    if (!displayName.trim()) {
      toaster.create({ type: 'error', title: '请填写渠道名称' })
      return
    }
    const requiredFields = fields.filter((f) => f.required)
    for (const f of requiredFields) {
      if (!settings[f.key]?.trim()) {
        toaster.create({ type: 'error', title: `请填写 ${f.label}` })
        return
      }
    }
    setSaving(true)
    try {
      const settingsStr = JSON.stringify(settings)
      if (editing) {
        const req: ChannelUpdateRequest = {
          id: editing.id,
          displayName,
          isEnabled,
          settings: settingsStr,
        }
        await updateChannel(req)
      } else {
        const req: ChannelCreateRequest = {
          displayName,
          channelType,
          isEnabled,
          settings: settingsStr,
        }
        await createChannel(req)
      }
      toaster.create({ type: 'success', title: editing ? '更新成功' : '创建成功' })
      onSaved()
      onClose()
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSaving(false)
    }
  }

  return (
    <AppDialog
      open={open}
      onClose={onClose}
      title={editing ? '编辑渠道' : `新建 ${TYPE_LABELS[channelType] ?? channelType} 渠道`}
      contentProps={{ maxW: '480px' }}
      bodyProps={{ maxH: '90vh', overflowY: 'auto' }}
      footer={(
        <>
          <Button variant="outline" onClick={onClose}>取消</Button>
          <Button colorPalette="blue" loading={saving} onClick={handleSave}>保存</Button>
        </>
      )}
    >
      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">渠道名称 *</Text>
        <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="输入渠道名称" />
      </Box>

      {fields.map((f) => (
        <Box key={f.key} mb="3">
          <Text fontSize="sm" mb="1" fontWeight="medium">{f.label}{f.required ? ' *' : ''}</Text>
          {f.select ? (
            <Select.Root
              value={settings[f.key] ? [settings[f.key]] : ['websocket']}
              onValueChange={(v) => setSettings((prev) => ({ ...prev, [f.key]: v.value[0] ?? '' }))}
              collection={connectionModeCollection}
            >
              <Select.Trigger><Select.ValueText /></Select.Trigger>
              <Portal>
                <Select.Positioner>
                  <Select.Content>
                    {CONNECTION_MODE_OPTIONS.map((o) => (
                      <Select.Item key={o.value} item={o}>{o.label}</Select.Item>
                    ))}
                  </Select.Content>
                </Select.Positioner>
              </Portal>
            </Select.Root>
          ) : (
            <Input
              type={f.type ?? 'text'}
              value={settings[f.key] ?? ''}
              onChange={(e) => setSettings((prev) => ({ ...prev, [f.key]: e.target.value }))}
              placeholder={f.label}
            />
          )}
        </Box>
      ))}
    </AppDialog>
  )
}

// ─── 发布消息弹窗 ─────────────────────────────────────────────────────────────
interface PublishDialogProps {
  open: boolean
  channel: ChannelConfig | null
  sessions: SessionInfo[]
  onClose: () => void
}

function PublishDialog({ open, channel, sessions, onClose }: PublishDialogProps) {
  const [sessionId, setSessionId] = useState('')
  const [content, setContent] = useState('')
  const [sending, setSending] = useState(false)

  const sessionCollection = createListCollection({
    items: sessions.map((s) => ({ value: s.id, label: s.title })),
  })

  useEffect(() => {
    if (open) { setSessionId(''); setContent('') }
  }, [open])

  if (!open || !channel) return null

  const handleSend = async () => {
    if (!content.trim()) return
    setSending(true)
    try {
      await publishChannelMessage(channel.id, { targetId: sessionId, content })
      toaster.create({ type: 'success', title: '消息已发送' })
      onClose()
    } catch (err) {
      toaster.create({ type: 'error', title: '发送失败', description: String(err) })
    } finally {
      setSending(false)
    }
  }

  return (
    <AppDialog
      open={open}
      onClose={onClose}
      title="发布消息"
      contentProps={{ maxW: '420px' }}
      footer={(
        <>
          <Button variant="ghost" onClick={onClose}>取消</Button>
          <Button colorPalette="green" loading={sending} disabled={!content.trim()} onClick={handleSend}>
            <Send size={14} /> 发送
          </Button>
        </>
      )}
    >
      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">目标会话（可选）</Text>
        <Select.Root
          value={sessionId ? [sessionId] : []}
          onValueChange={(v) => setSessionId(v.value[0] ?? '')}
          collection={sessionCollection}
        >
          <Select.Trigger><Select.ValueText placeholder="不指定会话" /></Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {sessions.map((s) => (
                  <Select.Item key={s.id} item={{ value: s.id, label: s.title }}>{s.title}</Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>
      <Box>
        <Text fontSize="sm" mb="1" fontWeight="medium">消息内容 *</Text>
        <Textarea value={content} onChange={(e) => setContent(e.target.value)} placeholder="输入消息内容" minH="100px" />
      </Box>
    </AppDialog>
  )
}

// ─── 主组件 ───────────────────────────────────────────────────────────────────
export default function ChannelsPage() {
  const [channelTypes, setChannelTypes] = useState<ChannelTypeInfo[]>([])
  const [selectedType, setSelectedType] = useState<ChannelType>('feishu')
  const [allChannels, setAllChannels] = useState<ChannelConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [typeLoading, setTypeLoading] = useState(true)
  const [sessions, setSessions] = useState<SessionInfo[]>([])
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ChannelConfig | null>(null)
  const [publishChannel, setPublishChannel] = useState<ChannelConfig | null>(null)
  const [healthMap, setHealthMap] = useState<Record<string, ChannelHealth>>({})
  const [statsMap, setStatsMap] = useState<Record<string, ChannelStats>>({})
  const [testing, setTesting] = useState<Record<string, boolean>>({})
  const [deleteTarget, setDeleteTarget] = useState<ChannelConfig | null>(null)

  const loadChannels = useCallback(async (type: ChannelType) => {
    setLoading(true)
    try {
      const all = await listChannels()
      const filtered = all.filter((c) => c.channelType === type)
      setAllChannels(all)

      // 加载飞书渠道健康/统计数据
      if (type === 'feishu') {
        const healthEntries = await Promise.all(
          filtered.map(async (c) => {
            try { return [c.id, await getChannelHealth(c.id)] as const } catch { return null }
          }),
        )
        const statsEntries = await Promise.all(
          filtered.map(async (c) => {
            try { return [c.id, await getChannelStats(c.id)] as const } catch { return null }
          }),
        )
        setHealthMap(Object.fromEntries(healthEntries.filter(Boolean) as [string, ChannelHealth][]))
        setStatsMap(Object.fromEntries(statsEntries.filter(Boolean) as [string, ChannelStats][]))
      }
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    const init = async () => {
      setTypeLoading(true)
      try {
        const [types, ss] = await Promise.all([
          getChannelTypes(),
          listSessions(),
        ])
        setChannelTypes(types)
        if (types.length > 0) setSelectedType(types[0].type as ChannelType)
        setSessions(ss)
      } finally {
        setTypeLoading(false)
      }
    }
    init()
  }, [])

  useEffect(() => {
    loadChannels(selectedType)
  }, [selectedType, loadChannels])

  const handleToggle = async (c: ChannelConfig, enabled: boolean) => {
    try {
      await updateChannel({ id: c.id, isEnabled: enabled })
      setAllChannels((prev) => prev.map((x) => x.id === c.id ? { ...x, isEnabled: enabled } : x))
    } catch (err) {
      toaster.create({ type: 'error', title: '操作失败', description: String(err) })
    }
  }

  const handleDelete = async (c: ChannelConfig) => {
    try {
      await deleteChannel(c.id)
      setAllChannels((prev) => prev.filter((x) => x.id !== c.id))
      toaster.create({ type: 'success', title: '已删除' })
    } catch (err) {
      toaster.create({ type: 'error', title: '删除失败', description: String(err) })
    } finally {
      setDeleteTarget(null)
    }
  }

  const handleTest = async (c: ChannelConfig) => {
    setTesting((prev) => ({ ...prev, [c.id]: true }))
    try {
      const result = await testChannel(c.id)
      if (result.success) {
        toaster.create({ type: 'success', title: '连接测试成功', description: `${result.message} (${result.latencyMs}ms)` })
      } else {
        toaster.create({ type: 'error', title: '连接测试失败', description: result.message })
      }
    } catch (err) {
      toaster.create({ type: 'error', title: '测试失败', description: String(err) })
    } finally {
      setTesting((prev) => ({ ...prev, [c.id]: false }))
    }
  }

  const selectedTypeInfo = channelTypes.find((t) => t.type === selectedType)
  const channels = allChannels.filter((c) => c.channelType === selectedType)

  return (
    <Flex h="full" overflow="hidden">
      {/* ── 左侧类型列表 ── */}
      <Box
        w="180px" flexShrink={0} borderRightWidth="1px"
        display="flex" flexDir="column" overflow="hidden"
      >
        <Flex px="3" py="3" align="center" borderBottomWidth="1px">
          <Text fontWeight="semibold" fontSize="sm">渠道类型</Text>
        </Flex>

        {typeLoading ? (
          <Flex justify="center" py="8"><Spinner /></Flex>
        ) : (
          <Box flex="1" overflowY="auto" py="1">
            {channelTypes.map((t) => {
              const isActive = selectedType === t.type
              return (
                <Flex
                  key={t.type}
                  px="3" py="2.5" align="center" gap="2"
                  cursor="pointer"
                  bg={isActive ? 'blue.50' : undefined}
                  _dark={{ bg: isActive ? 'blue.900' : undefined }}
                  _hover={{ bg: isActive ? 'blue.50' : 'gray.50', _dark: { bg: isActive ? 'blue.900' : 'gray.700' } }}
                  onClick={() => setSelectedType(t.type as ChannelType)}
                >
                  <Badge colorPalette={TYPE_COLORS[t.type] ?? 'gray'} size="sm">
                    {TYPE_LABELS[t.type] ?? t.type}
                  </Badge>
                  <Text fontSize="sm" flex="1" color={isActive ? 'blue.600' : undefined}>
                    {t.displayName}
                  </Text>
                  {allChannels.filter((c) => c.channelType === t.type).length > 0 && (
                    <Badge size="sm" colorPalette="blue" variant="solid">
                      {allChannels.filter((c) => c.channelType === t.type).length}
                    </Badge>
                  )}
                </Flex>
              )
            })}
          </Box>
        )}
      </Box>

      {/* ── 右侧渠道列表 ── */}
      <Box flex="1" overflowY="auto" p="4">
        <Flex align="center" justify="space-between" mb="4">
          <Box>
            <Text fontWeight="semibold">{selectedTypeInfo?.displayName ?? selectedType} 渠道</Text>
            <Text fontSize="xs" color="gray.500">已配置的渠道实例</Text>
          </Box>
          <Flex gap="2">
            <Button size="sm" variant="ghost" onClick={() => loadChannels(selectedType)}>
              <RefreshCw size={14} />
            </Button>
            {selectedTypeInfo?.canCreate && (
              <Button size="sm" colorPalette="blue" onClick={() => { setEditing(null); setDialogOpen(true) }}>
                <Plus size={14} /> 添加渠道
              </Button>
            )}
          </Flex>
        </Flex>

        {loading ? (
          <Flex justify="center" py="8"><Spinner /></Flex>
        ) : channels.length === 0 ? (
          <Flex align="center" justify="center" py="12" flexDir="column" gap="3" color="gray.400">
            <Radio size={40} />
            <Text>暂无渠道配置</Text>
            {selectedTypeInfo?.canCreate && (
              <Button colorPalette="blue" size="sm" onClick={() => { setEditing(null); setDialogOpen(true) }}>
                <Plus size={14} /> 添加渠道
              </Button>
            )}
          </Flex>
        ) : (
          <Flex flexDir="column" gap="3">
            {channels.map((c) => {
              const health = healthMap[c.id]
              const stats = statsMap[c.id]
              const connectionOk = health ? health.connectionStatus === 'connected' : null

              return (
                <Card.Root key={c.id} opacity={c.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline">
                  <Card.Body p="4">
                    {/* 头部 */}
                    <Flex align="center" gap="2" mb="2">
                      <Text fontWeight="semibold" flex="1">{c.displayName}</Text>
                      {c.channelType === 'feishu' && connectionOk !== null && (
                        <Box
                          w="8px" h="8px" borderRadius="full"
                          bg={connectionOk ? 'green.400' : 'red.400'}
                          title={connectionOk ? '连接正常' : '连接异常'}
                        />
                      )}
                      <Badge colorPalette={TYPE_COLORS[c.channelType] ?? 'gray'} size="sm">
                        {TYPE_LABELS[c.channelType] ?? c.channelType}
                      </Badge>
                    </Flex>

                    {/* 飞书统计 */}
                    {stats && (
                      <Flex gap="4" mb="2" fontSize="xs" color="gray.500">
                        <Text color={stats.signatureFailures > 0 ? 'red.500' : undefined}>
                          签名失败: {stats.signatureFailures}
                        </Text>
                        <Text color={stats.aiCallFailures > 0 ? 'red.500' : undefined}>
                          AI失败: {stats.aiCallFailures}
                        </Text>
                        <Text color={stats.replyFailures > 0 ? 'red.500' : undefined}>
                          回复失败: {stats.replyFailures}
                        </Text>
                      </Flex>
                    )}

                    {/* 底部操作 */}
                    <Flex align="center" justify="space-between" mt="2">
                      <Switch.Root size="sm" checked={c.isEnabled} onCheckedChange={(d) => handleToggle(c, d.checked)}>
                        <Switch.HiddenInput />
                        <Switch.Control><Switch.Thumb /></Switch.Control>
                        <Switch.Label fontSize="xs">{c.isEnabled ? '启用' : '停用'}</Switch.Label>
                      </Switch.Root>

                      <Flex gap="1">
                        <Button
                          size="xs" variant="ghost"
                          loading={testing[c.id]}
                          onClick={() => handleTest(c)}
                        >
                          测试
                        </Button>
                        {c.channelType === 'feishu' && (
                          <Button size="xs" variant="ghost" colorPalette="green" onClick={() => setPublishChannel(c)}>
                            <Send size={11} /> 发送
                          </Button>
                        )}
                        <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => { setEditing(c); setDialogOpen(true) }}>
                          <Edit size={11} /> 编辑
                        </Button>
                        <Button size="xs" variant="ghost" colorPalette="red" aria-label={`删除渠道 ${c.displayName}`} onClick={() => setDeleteTarget(c)}>
                          <Trash2 size={11} />
                        </Button>
                      </Flex>
                    </Flex>
                  </Card.Body>
                </Card.Root>
              )
            })}
          </Flex>
        )}
      </Box>

      {/* ── 弹窗 ── */}
      <ChannelDialog
        open={dialogOpen}
        channelType={selectedType}
        editing={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => loadChannels(selectedType)}
      />

      <PublishDialog
        open={!!publishChannel}
        channel={publishChannel}
        sessions={sessions}
        onClose={() => setPublishChannel(null)}
      />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除渠道"
        description={`确认删除渠道「${deleteTarget?.displayName}」？`}
        confirmText="删除"
      />
    </Flex>
  )
}
