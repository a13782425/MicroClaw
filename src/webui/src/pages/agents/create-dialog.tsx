import { useState } from 'react'
import {
  Box, Text, Button, Input, Textarea, VStack, Dialog,
} from '@chakra-ui/react'
import {
  createAgent,
  type AgentCreateRequest,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'

interface CreateDialogProps {
  open: boolean
  onClose: () => void
  onCreated: () => void
}

export function CreateDialog({ open, onClose, onCreated }: CreateDialogProps) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)

  const reset = () => {
    setName('')
    setDescription('')
  }

  const submit = async () => {
    if (!name.trim()) return
    setSaving(true)
    try {
      const request: AgentCreateRequest = { name: name.trim(), description: description.trim() || undefined }
      await createAgent(request)
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
