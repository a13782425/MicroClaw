import { useEffect, useState } from 'react'
import {
  Box, Text, Button, Input, Select, Portal,
} from '@chakra-ui/react'
import {
  createChannel,
  updateChannel,
  type ChannelConfig,
  type ChannelCreateRequest,
  type ChannelUpdateRequest,
  type ChannelType,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'
import {
  CHANNEL_FIELDS,
  CONNECTION_MODE_OPTIONS,
  TYPE_LABELS,
  connectionModeCollection,
  parseSettings,
} from './channel-constants'

interface ChannelDialogProps {
  open: boolean
  channelType: ChannelType
  editing: ChannelConfig | null
  onClose: () => void
  onSaved: () => void
}

export function ChannelDialog({ open, channelType, editing, onClose, onSaved }: ChannelDialogProps) {
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
    const requiredFields = fields.filter((field) => field.required)
    for (const field of requiredFields) {
      if (!settings[field.key]?.trim()) {
        toaster.create({ type: 'error', title: `请填写 ${field.label}` })
        return
      }
    }
    setSaving(true)
    try {
      const settingsStr = JSON.stringify(settings)
      if (editing) {
        const request: ChannelUpdateRequest = {
          id: editing.id,
          displayName,
          isEnabled,
          settings: settingsStr,
        }
        await updateChannel(request)
      } else {
        const request: ChannelCreateRequest = {
          displayName,
          channelType,
          isEnabled,
          settings: settingsStr,
        }
        await createChannel(request)
      }
      toaster.create({ type: 'success', title: editing ? '更新成功' : '创建成功' })
      onSaved()
      onClose()
    } catch (error) {
      toaster.create({ type: 'error', title: '保存失败', description: String(error) })
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

      {fields.map((field) => (
        <Box key={field.key} mb="3">
          <Text fontSize="sm" mb="1" fontWeight="medium">{field.label}{field.required ? ' *' : ''}</Text>
          {field.select ? (
            <Select.Root
              value={settings[field.key] ? [settings[field.key]] : ['websocket']}
              onValueChange={(value) => setSettings((prev) => ({ ...prev, [field.key]: value.value[0] ?? '' }))}
              collection={connectionModeCollection}
            >
              <Select.Trigger><Select.ValueText /></Select.Trigger>
              <Portal>
                <Select.Positioner>
                  <Select.Content>
                    {CONNECTION_MODE_OPTIONS.map((option) => (
                      <Select.Item key={option.value} item={option}>{option.label}</Select.Item>
                    ))}
                  </Select.Content>
                </Select.Positioner>
              </Portal>
            </Select.Root>
          ) : (
            <Input
              type={field.type ?? 'text'}
              value={settings[field.key] ?? ''}
              onChange={(e) => setSettings((prev) => ({ ...prev, [field.key]: e.target.value }))}
              placeholder={field.label}
            />
          )}
        </Box>
      ))}
    </AppDialog>
  )
}
