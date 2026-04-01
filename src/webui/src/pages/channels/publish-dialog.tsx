import { useEffect, useState } from 'react'
import {
  Box, Text, Button, Textarea, Select, Portal, createListCollection,
} from '@chakra-ui/react'
import { Send } from 'lucide-react'
import {
  publishChannelMessage,
  type ChannelConfig,
  type SessionInfo,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'

interface PublishDialogProps {
  open: boolean
  channel: ChannelConfig | null
  sessions: SessionInfo[]
  onClose: () => void
}

export function PublishDialog({ open, channel, sessions, onClose }: PublishDialogProps) {
  const [sessionId, setSessionId] = useState('')
  const [content, setContent] = useState('')
  const [sending, setSending] = useState(false)

  const sessionCollection = createListCollection({
    items: sessions.map((session) => ({ value: session.id, label: session.title })),
  })

  useEffect(() => {
    if (open) {
      setSessionId('')
      setContent('')
    }
  }, [open])

  if (!open || !channel) return null

  const handleSend = async () => {
    if (!content.trim()) return
    setSending(true)
    try {
      await publishChannelMessage(channel.id, { targetId: sessionId, content })
      toaster.create({ type: 'success', title: '消息已发送' })
      onClose()
    } catch (error) {
      toaster.create({ type: 'error', title: '发送失败', description: String(error) })
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
          onValueChange={(value) => setSessionId(value.value[0] ?? '')}
          collection={sessionCollection}
        >
          <Select.Trigger><Select.ValueText placeholder="不指定会话" /></Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {sessions.map((session) => (
                  <Select.Item key={session.id} item={{ value: session.id, label: session.title }}>{session.title}</Select.Item>
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
