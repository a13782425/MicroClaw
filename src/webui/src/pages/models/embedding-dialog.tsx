import { useEffect, useState } from 'react'
import {
  Box, Text, Button, Input, Textarea, Select, Portal, SimpleGrid,
} from '@chakra-ui/react'
import {
  createProvider,
  updateProvider,
  type ProviderConfig,
  type ProviderProtocol,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'
import {
  buildEmbeddingCapabilities,
  defaultEmbeddingForm,
  embeddingProtocolCollection,
  EMBEDDING_PROTOCOL_OPTIONS,
  EmbeddingFormState,
} from './model-form-helpers'

interface EmbeddingDialogProps {
  open: boolean
  editing: ProviderConfig | null
  onClose: () => void
  onSaved: () => void
}

export function EmbeddingDialog({ open, editing, onClose, onSaved }: EmbeddingDialogProps) {
  const [form, setForm] = useState<EmbeddingFormState>(defaultEmbeddingForm())
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (open) {
      if (editing) {
        const caps = editing.capabilities
        setForm({
          displayName: editing.displayName,
          protocol: editing.protocol,
          baseUrl: editing.baseUrl ?? '',
          apiKey: '',
          modelName: editing.modelName,
          isEnabled: editing.isEnabled,
          maxInputTokens: caps?.maxInputTokens?.toString() ?? '',
          outputDimensions: caps?.outputDimensions?.toString() ?? '',
          inputPricePerMToken: caps?.inputPricePerMToken?.toString() ?? '',
          notes: caps?.notes ?? '',
        })
      } else {
        setForm(defaultEmbeddingForm())
      }
    }
  }, [open, editing])

  if (!open) return null

  const set = (key: keyof EmbeddingFormState, value: unknown) => setForm((prev) => ({ ...prev, [key]: value }))

  const handleSave = async () => {
    if (!form.displayName.trim() || !form.modelName.trim()) {
      toaster.create({ type: 'error', title: '请填写显示名称和模型名称' })
      return
    }
    if (!editing && !form.apiKey.trim()) {
      toaster.create({ type: 'error', title: '请填写 API Key' })
      return
    }
    setSaving(true)
    try {
      const capabilities = buildEmbeddingCapabilities(form)
      if (editing) {
        await updateProvider({
          id: editing.id,
          displayName: form.displayName,
          protocol: form.protocol,
          modelType: 'embedding',
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey || undefined,
          modelName: form.modelName,
          isEnabled: form.isEnabled,
          capabilities,
        })
      } else {
        await createProvider({
          displayName: form.displayName,
          protocol: form.protocol,
          modelType: 'embedding',
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey,
          modelName: form.modelName,
          isEnabled: form.isEnabled,
          capabilities,
        })
      }
      toaster.create({ type: 'success', title: editing ? '更新成功' : '添加成功' })
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
      title={editing ? '编辑嵌入模型' : '添加提供方'}
      contentProps={{ maxW: '480px' }}
      footer={(
        <>
          <Button variant="outline" onClick={onClose}>取消</Button>
          <Button
            loading={saving}
            onClick={handleSave}
            bg="var(--mc-accent)"
            color="var(--mc-send-button-color)"
            _hover={{ opacity: 0.92 }}
          >
            保存
          </Button>
        </>
      )}
    >
      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">显示名称 *</Text>
        <Input value={form.displayName} onChange={(e) => set('displayName', e.target.value)} placeholder="例如：text-embedding-3-small" />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">协议类型</Text>
        <Select.Root
          value={[form.protocol]}
          onValueChange={(value) => set('protocol', value.value[0] as ProviderProtocol)}
          collection={embeddingProtocolCollection}
        >
          <Select.Trigger><Select.ValueText /></Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {EMBEDDING_PROTOCOL_OPTIONS.map((option) => (
                  <Select.Item key={option.value} item={option}>{option.label}</Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">Base URL</Text>
        <Input value={form.baseUrl} onChange={(e) => set('baseUrl', e.target.value)} placeholder="留空使用官方默认端点" />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">API Key {!editing && '*'}</Text>
        <Input
          type="password"
          value={form.apiKey}
          onChange={(e) => set('apiKey', e.target.value)}
          placeholder={editing ? '留空保留原有 Key' : '请输入 API Key'}
        />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">模型名称 *</Text>
        <Input
          value={form.modelName}
          onChange={(e) => set('modelName', e.target.value)}
          placeholder="text-embedding-3-small"
        />
      </Box>

      <SimpleGrid columns={2} gap="3" mb="3">
        <Box>
          <Text fontSize="sm" mb="1" fontWeight="medium">最大输入 Tokens</Text>
          <Input
            type="number"
            min={1}
            value={form.maxInputTokens}
            onChange={(e) => set('maxInputTokens', e.target.value)}
            placeholder="8192"
          />
        </Box>
        <Box>
          <Text fontSize="sm" mb="1" fontWeight="medium">向量维度</Text>
          <Input
            type="number"
            min={1}
            value={form.outputDimensions}
            onChange={(e) => set('outputDimensions', e.target.value)}
            placeholder="1536"
          />
        </Box>
      </SimpleGrid>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">输入价格（$ / 1M tokens）</Text>
        <Input
          type="number"
          step="0.001"
          min={0}
          value={form.inputPricePerMToken}
          onChange={(e) => set('inputPricePerMToken', e.target.value)}
          placeholder="0.020"
        />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">备注</Text>
        <Textarea
          rows={2}
          value={form.notes}
          onChange={(e) => set('notes', e.target.value)}
          placeholder="可选备注信息"
        />
      </Box>
    </AppDialog>
  )
}
