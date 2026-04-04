import { useEffect, useState, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Switch, Spinner, Button, Card,
} from '@chakra-ui/react'
import { Plus, Trash2, Edit, RefreshCw, Send, Radio } from 'lucide-react'
import {
  getChannelTypes,
  listChannels,
  updateChannel,
  deleteChannel,
  testChannel,
  getChannelHealth,
  getChannelStats,
  listSessions,
  type ChannelConfig,
  type ChannelTypeInfo,
  type ChannelHealth,
  type ChannelStats,
  type SessionInfo,
  type ChannelType,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { TYPE_BADGE_BG, TYPE_BADGE_FG, TYPE_LABELS } from './channel-constants'
import { ChannelDialog } from './channel-dialog'
import { PublishDialog } from './publish-dialog'

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
      const filtered = all.filter((channel) => channel.channelType === type)
      setAllChannels(all)

      if (type === 'feishu') {
        const healthEntries = await Promise.all(
          filtered.map(async (channel) => {
            try { return [channel.id, await getChannelHealth(channel.id)] as const } catch { return null }
          }),
        )
        const statsEntries = await Promise.all(
          filtered.map(async (channel) => {
            try { return [channel.id, await getChannelStats(channel.id)] as const } catch { return null }
          }),
        )
        setHealthMap(Object.fromEntries(healthEntries.filter(Boolean) as [string, ChannelHealth][]))
        setStatsMap(Object.fromEntries(statsEntries.filter(Boolean) as [string, ChannelStats][]))
      } else {
        setHealthMap({})
        setStatsMap({})
      }
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    const init = async () => {
      setTypeLoading(true)
      try {
        const [types, sessionList] = await Promise.all([
          getChannelTypes(),
          listSessions(),
        ])
        setChannelTypes(types)
        if (types.length > 0) setSelectedType(types[0].type as ChannelType)
        setSessions(sessionList)
      } finally {
        setTypeLoading(false)
      }
    }
    init()
  }, [])

  useEffect(() => {
    loadChannels(selectedType)
  }, [selectedType, loadChannels])

  const handleToggle = async (channel: ChannelConfig, enabled: boolean) => {
    try {
      await updateChannel({ id: channel.id, isEnabled: enabled })
      setAllChannels((prev) => prev.map((item) => item.id === channel.id ? { ...item, isEnabled: enabled } : item))
    } catch (error) {
      toaster.create({ type: 'error', title: '操作失败', description: String(error) })
    }
  }

  const handleDelete = async (channel: ChannelConfig) => {
    try {
      await deleteChannel(channel.id)
      setAllChannels((prev) => prev.filter((item) => item.id !== channel.id))
      toaster.create({ type: 'success', title: '已删除' })
    } catch (error) {
      toaster.create({ type: 'error', title: '删除失败', description: String(error) })
    } finally {
      setDeleteTarget(null)
    }
  }

  const handleTest = async (channel: ChannelConfig) => {
    setTesting((prev) => ({ ...prev, [channel.id]: true }))
    try {
      const result = await testChannel(channel.id)
      if (result.success) {
        toaster.create({ type: 'success', title: '连接测试成功', description: `${result.message} (${result.latencyMs}ms)` })
      } else {
        toaster.create({ type: 'error', title: '连接测试失败', description: result.message })
      }
    } catch (error) {
      toaster.create({ type: 'error', title: '测试失败', description: String(error) })
    } finally {
      setTesting((prev) => ({ ...prev, [channel.id]: false }))
    }
  }

  const selectedTypeInfo = channelTypes.find((typeInfo) => typeInfo.type === selectedType)
  const channels = allChannels.filter((channel) => channel.channelType === selectedType)

  return (
    <Flex h="full" overflow="hidden">
      <Box w="180px" flexShrink={0} borderRightWidth="1px" display="flex" flexDir="column" overflow="hidden">
        <Flex px="3" py="3" align="center" borderBottomWidth="1px">
          <Text fontWeight="semibold" fontSize="sm">渠道类型</Text>
        </Flex>

        {typeLoading ? (
          <Flex justify="center" py="8"><Spinner /></Flex>
        ) : (
          <Box flex="1" overflowY="auto" py="1">
            {channelTypes.map((typeInfo) => {
              const isActive = selectedType === typeInfo.type
              const count = allChannels.filter((channel) => channel.channelType === typeInfo.type).length
              return (
                <Flex
                  key={typeInfo.type}
                  px="3"
                  py="2.5"
                  align="center"
                  gap="2"
                  cursor="pointer"
                  bg={isActive ? 'var(--mc-selected-bg)' : 'transparent'}
                  borderLeftWidth="2px"
                  borderLeftColor={isActive ? 'var(--mc-sidebar-active-border)' : 'transparent'}
                  _hover={{ bg: isActive ? 'var(--mc-selected-hover-bg)' : 'var(--mc-card-hover)' }}
                  onClick={() => setSelectedType(typeInfo.type as ChannelType)}
                >
                  <Badge size="sm" bg={TYPE_BADGE_BG[typeInfo.type] ?? 'var(--mc-card-hover)'} color={TYPE_BADGE_FG[typeInfo.type] ?? 'var(--mc-text-muted)'}>
                    {TYPE_LABELS[typeInfo.type] ?? typeInfo.type}
                  </Badge>
                  <Text fontSize="sm" flex="1" color={isActive ? 'var(--mc-primary)' : 'var(--mc-text)'}>{typeInfo.displayName}</Text>
                  {count > 0 && (
                    <Badge size="sm" bg="var(--mc-primary-soft)" color="var(--mc-primary)" borderRadius="var(--mc-badge-radius)">{count}</Badge>
                  )}
                </Flex>
              )
            })}
          </Box>
        )}
      </Box>

      <Box flex="1" overflowY="auto" p="4">
        <Flex align="center" justify="space-between" mb="4">
          <Box>
            <Text fontWeight="semibold">{selectedTypeInfo?.displayName ?? selectedType} 渠道</Text>
            <Text fontSize="xs" color="var(--mc-text-muted)">已配置的渠道实例</Text>
          </Box>
          <Flex gap="2">
            <Button size="sm" variant="ghost" data-mc-refresh="true" onClick={() => loadChannels(selectedType)}>
              <RefreshCw size={14} />
            </Button>
            {selectedTypeInfo?.canCreate && (
              <Button
                size="sm"
                bg="var(--mc-send-button-bg)"
                color="var(--mc-send-button-color)"
                _hover={{ opacity: 0.92 }}
                onClick={() => { setEditing(null); setDialogOpen(true) }}
              >
                <Plus size={14} /> 添加渠道
              </Button>
            )}
          </Flex>
        </Flex>

        {loading ? (
          <Flex justify="center" py="8"><Spinner /></Flex>
        ) : channels.length === 0 ? (
          <Flex align="center" justify="center" py="12" flexDir="column" gap="3" color="var(--mc-text-muted)">
            <Radio size={40} />
            <Text>暂无渠道配置</Text>
            {selectedTypeInfo?.canCreate && (
              <Button
                size="sm"
                bg="var(--mc-send-button-bg)"
                color="var(--mc-send-button-color)"
                _hover={{ opacity: 0.92 }}
                onClick={() => { setEditing(null); setDialogOpen(true) }}
              >
                <Plus size={14} /> 添加渠道
              </Button>
            )}
          </Flex>
        ) : (
          <Flex flexDir="column" gap="3">
            {channels.map((channel) => {
              const health = healthMap[channel.id]
              const stats = statsMap[channel.id]
              const connectionOk = health ? health.connectionStatus === 'connected' : null

              return (
                <Card.Root key={channel.id} opacity={channel.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline" bg="var(--mc-card)" borderColor="var(--mc-border)">
                  <Card.Body p="4">
                    <Flex align="center" gap="2" mb="2">
                      <Text fontWeight="semibold" flex="1" color="var(--mc-text)">{channel.displayName}</Text>
                      {channel.channelType === 'feishu' && connectionOk !== null && (
                        <Box w="8px" h="8px" borderRadius="full" bg={connectionOk ? 'var(--mc-success)' : 'var(--mc-danger)'} title={connectionOk ? '连接正常' : '连接异常'} />
                      )}
                      <Badge size="sm" bg={TYPE_BADGE_BG[channel.channelType] ?? 'var(--mc-card-hover)'} color={TYPE_BADGE_FG[channel.channelType] ?? 'var(--mc-text-muted)'}>
                        {TYPE_LABELS[channel.channelType] ?? channel.channelType}
                      </Badge>
                    </Flex>

                    {stats && (
                      <Flex gap="4" mb="2" fontSize="xs" color="var(--mc-text-muted)">
                        <Text color={stats.signatureFailures > 0 ? 'var(--mc-danger)' : undefined}>签名失败: {stats.signatureFailures}</Text>
                        <Text color={stats.aiCallFailures > 0 ? 'var(--mc-danger)' : undefined}>AI失败: {stats.aiCallFailures}</Text>
                        <Text color={stats.replyFailures > 0 ? 'var(--mc-danger)' : undefined}>回复失败: {stats.replyFailures}</Text>
                      </Flex>
                    )}

                    <Flex align="center" justify="space-between" mt="2">
                      <Switch.Root size="sm" checked={channel.isEnabled} onCheckedChange={(details) => handleToggle(channel, details.checked)}>
                        <Switch.HiddenInput />
                        <Switch.Control><Switch.Thumb /></Switch.Control>
                        <Switch.Label fontSize="xs" color="var(--mc-text)">{channel.isEnabled ? '启用' : '停用'}</Switch.Label>
                      </Switch.Root>

                      <Flex gap="1">
                        <Button size="xs" variant="ghost" loading={testing[channel.id]} onClick={() => handleTest(channel)}>
                          测试
                        </Button>
                        {channel.channelType === 'feishu' && (
                          <Button size="xs" variant="ghost" color="var(--mc-success)" _hover={{ bg: 'var(--mc-success-soft)' }} onClick={() => setPublishChannel(channel)}>
                            <Send size={11} /> 发送
                          </Button>
                        )}
                        <Button size="xs" variant="ghost" color="var(--mc-primary)" _hover={{ bg: 'var(--mc-primary-soft)' }} onClick={() => { setEditing(channel); setDialogOpen(true) }}>
                          <Edit size={11} /> 编辑
                        </Button>
                        <Button size="xs" variant="ghost" color="var(--mc-danger)" _hover={{ bg: 'var(--mc-danger-soft)' }} aria-label={`删除渠道 ${channel.displayName}`} onClick={() => setDeleteTarget(channel)}>
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

      <ChannelDialog
        open={dialogOpen}
        channelType={selectedType}
        editing={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => loadChannels(selectedType)}
      />

      <PublishDialog open={!!publishChannel} channel={publishChannel} sessions={sessions} onClose={() => setPublishChannel(null)} />

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
