import { useEffect, useState } from 'react'
import {
  Box, Flex, Text, Button, Switch, Input, Textarea, Select, Portal,
  SimpleGrid, Collapsible, CheckboxCard,
} from '@chakra-ui/react'
import { ChevronDown } from 'lucide-react'
import {
  createProvider,
  updateProvider,
  type ProviderConfig,
  type ProviderCreateRequest,
  type ProviderProtocol,
  type ProviderUpdateRequest,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'
import {
  buildChatCapabilities,
  defaultChatForm,
  hasNonDefaultCapabilities,
  hasNonDefaultPricing,
  INPUT_MODALITIES,
  OUTPUT_MODALITIES,
  LATENCY_TIER_OPTIONS,
  ChatFormState,
  latencyTierCollection,
  protocolCollection,
  PROTOCOL_OPTIONS,
} from './model-form-helpers'

interface ChatProviderDialogProps {
  open: boolean
  editing: ProviderConfig | null
  onClose: () => void
  onSaved: () => void
}

export function ChatProviderDialog({ open, editing, onClose, onSaved }: ChatProviderDialogProps) {
  const [form, setForm] = useState<ChatFormState>(defaultChatForm())
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
          maxOutputTokens: editing.maxOutputTokens,
          isEnabled: editing.isEnabled,
          inputImage: caps?.inputImage ?? false,
          inputAudio: caps?.inputAudio ?? false,
          inputVideo: caps?.inputVideo ?? false,
          inputFile: caps?.inputFile ?? false,
          outputImage: caps?.outputImage ?? false,
          outputAudio: caps?.outputAudio ?? false,
          outputVideo: caps?.outputVideo ?? false,
          supportsFunctionCalling: caps?.supportsFunctionCalling ?? false,
          supportsResponsesApi: caps?.supportsResponsesApi ?? false,
          inputPricePerMToken: caps?.inputPricePerMToken?.toString() ?? '',
          outputPricePerMToken: caps?.outputPricePerMToken?.toString() ?? '',
          cacheInputPricePerMToken: caps?.cacheInputPricePerMToken?.toString() ?? '',
          cacheOutputPricePerMToken: caps?.cacheOutputPricePerMToken?.toString() ?? '',
          notes: caps?.notes ?? '',
          qualityScore: caps?.qualityScore ?? 50,
          latencyTier: caps?.latencyTier ?? 'Medium',
        })
      } else {
        setForm(defaultChatForm())
      }
    }
  }, [open, editing])

  if (!open) return null

  const set = (key: keyof ChatFormState, value: unknown) => setForm((prev) => ({ ...prev, [key]: value }))

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
      const capabilities = buildChatCapabilities(form)
      if (editing) {
        const request: ProviderUpdateRequest = {
          id: editing.id,
          displayName: form.displayName,
          protocol: form.protocol,
          modelType: 'chat',
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey || undefined,
          modelName: form.modelName,
          maxOutputTokens: form.maxOutputTokens,
          isEnabled: form.isEnabled,
          capabilities,
        }
        await updateProvider(request)
      } else {
        const request: ProviderCreateRequest = {
          displayName: form.displayName,
          protocol: form.protocol,
          modelType: 'chat',
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey,
          modelName: form.modelName,
          maxOutputTokens: form.maxOutputTokens,
          isEnabled: form.isEnabled,
          capabilities,
        }
        await createProvider(request)
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
      title={editing ? '编辑聊天模型' : '添加提供方'}
      contentProps={{ maxW: '540px' }}
      footer={(
        <>
          <Button variant="outline" onClick={onClose}>取消</Button>
          <Button
            loading={saving}
            onClick={handleSave}
            bg="var(--mc-send-button-bg)"
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
        <Input value={form.displayName} onChange={(e) => set('displayName', e.target.value)} placeholder="例如：My GPT-4o" />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">协议类型 *</Text>
        <Select.Root
          value={[form.protocol]}
          onValueChange={(value) => set('protocol', value.value[0] as ProviderProtocol)}
          collection={protocolCollection}
        >
          <Select.Trigger><Select.ValueText /></Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {PROTOCOL_OPTIONS.map((option) => (
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
        <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">填写可接入兼容 API 或代理</Text>
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
          placeholder={form.protocol === 'openai' ? 'gpt-4o' : 'claude-opus-4-5'}
        />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">最大输出 Tokens</Text>
        <Input
          type="number"
          value={form.maxOutputTokens}
          onChange={(e) => set('maxOutputTokens', parseInt(e.target.value) || 8192)}
          min={256}
          max={131072}
        />
      </Box>

      <Collapsible.Root defaultOpen={editing ? hasNonDefaultCapabilities(editing.capabilities) : false}>
        <Collapsible.Trigger asChild>
          <Button variant="ghost" size="sm" w="full" justifyContent="space-between" mb="2">
            <Text fontSize="sm" fontWeight="medium">能力配置</Text>
            <ChevronDown size={14} />
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <Box borderWidth="1px" rounded="md" p="3" mb="3">
            <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">输入模态</Text>
            <Flex gap="3" flexWrap="wrap" mb="3">
              {INPUT_MODALITIES.map((modality) => (
                <CheckboxCard.Root
                  key={modality.key}
                  variant="subtle"
                  size="sm"
                  colorPalette="blue"
                  checked={form[modality.key as keyof ChatFormState] as boolean}
                  onCheckedChange={(e) => set(modality.key as keyof ChatFormState, !!e.checked)}
                >
                  <CheckboxCard.HiddenInput />
                  <CheckboxCard.Control>
                    <CheckboxCard.Indicator />
                    <CheckboxCard.Label>{modality.label}</CheckboxCard.Label>
                  </CheckboxCard.Control>
                </CheckboxCard.Root>
              ))}
            </Flex>

            <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">输出模态</Text>
            <Flex gap="3" flexWrap="wrap" mb="3">
              {OUTPUT_MODALITIES.map((modality) => (
                <CheckboxCard.Root
                  key={modality.key}
                  variant="subtle"
                  size="sm"
                  colorPalette="blue"
                  checked={form[modality.key as keyof ChatFormState] as boolean}
                  onCheckedChange={(e) => set(modality.key as keyof ChatFormState, !!e.checked)}
                >
                  <CheckboxCard.HiddenInput />
                  <CheckboxCard.Control>
                    <CheckboxCard.Indicator />
                    <CheckboxCard.Label>{modality.label}</CheckboxCard.Label>
                  </CheckboxCard.Control>
                </CheckboxCard.Root>
              ))}
            </Flex>

            <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">特殊能力</Text>
            <Flex gap="4">
              <Switch.Root size="sm" checked={form.supportsFunctionCalling} onCheckedChange={(details) => set('supportsFunctionCalling', details.checked)}>
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
                <Switch.Label fontSize="xs">Function Calling</Switch.Label>
              </Switch.Root>
              <Switch.Root size="sm" checked={form.supportsResponsesApi} onCheckedChange={(details) => set('supportsResponsesApi', details.checked)}>
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
                <Switch.Label fontSize="xs">Responses API</Switch.Label>
              </Switch.Root>
            </Flex>
          </Box>
        </Collapsible.Content>
      </Collapsible.Root>

      <Collapsible.Root defaultOpen={editing ? hasNonDefaultPricing(editing.capabilities) : false}>
        <Collapsible.Trigger asChild>
          <Button variant="ghost" size="sm" w="full" justifyContent="space-between" mb="2">
            <Text fontSize="sm" fontWeight="medium">价格 & 备注</Text>
            <ChevronDown size={14} />
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <Box borderWidth="1px" rounded="md" p="3" mb="3">
            <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">单价（$ / 1M tokens）</Text>
            <SimpleGrid columns={2} gap="3" mb="3">
              <Box>
                <Text fontSize="xs" mb="1">输入</Text>
                <Input size="sm" type="number" step="0.01" min={0}
                  value={form.inputPricePerMToken}
                  onChange={(e) => set('inputPricePerMToken', e.target.value)}
                  placeholder="0.00" />
              </Box>
              <Box>
                <Text fontSize="xs" mb="1">输出</Text>
                <Input size="sm" type="number" step="0.01" min={0}
                  value={form.outputPricePerMToken}
                  onChange={(e) => set('outputPricePerMToken', e.target.value)}
                  placeholder="0.00" />
              </Box>
              <Box>
                <Text fontSize="xs" mb="1">缓存输入</Text>
                <Input size="sm" type="number" step="0.01" min={0}
                  value={form.cacheInputPricePerMToken}
                  onChange={(e) => set('cacheInputPricePerMToken', e.target.value)}
                  placeholder="0.00" />
              </Box>
              <Box>
                <Text fontSize="xs" mb="1">缓存输出</Text>
                <Input size="sm" type="number" step="0.01" min={0}
                  value={form.cacheOutputPricePerMToken}
                  onChange={(e) => set('cacheOutputPricePerMToken', e.target.value)}
                  placeholder="0.00" />
              </Box>
            </SimpleGrid>
            <Text fontSize="xs" mb="1">备注</Text>
            <Textarea size="sm" rows={2}
              value={form.notes}
              onChange={(e) => set('notes', e.target.value)}
              placeholder="可选备注信息" />
          </Box>
        </Collapsible.Content>
      </Collapsible.Root>

      <Collapsible.Root>
        <Collapsible.Trigger asChild>
          <Button variant="ghost" size="sm" w="full" justifyContent="space-between" mb="2">
            <Text fontSize="sm" fontWeight="medium">路由策略权重</Text>
            <ChevronDown size={14} />
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <Box borderWidth="1px" rounded="md" p="3" mb="3">
            <Text fontSize="xs" color="var(--mc-text-muted)" mb="3">
              配置此 Provider 在自动路由时的优先权重。Agent 选择"质量优先"策略时按质量分排序；选择"延迟优先"策略时按延迟层级排序。
            </Text>
            <SimpleGrid columns={2} gap="3">
              <Box>
                <Text fontSize="xs" mb="1">质量评分（0-100）</Text>
                <Input
                  size="sm"
                  type="number"
                  min={0}
                  max={100}
                  value={form.qualityScore}
                  onChange={(e) => set('qualityScore', Math.min(100, Math.max(0, parseInt(e.target.value) || 0)))}
                  placeholder="50"
                />
                <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">值越高"质量优先"时越优先</Text>
              </Box>
              <Box>
                <Text fontSize="xs" mb="1">延迟层级</Text>
                <Select.Root
                  value={[form.latencyTier]}
                  onValueChange={(value) => set('latencyTier', value.value[0])}
                  collection={latencyTierCollection}
                  size="sm"
                >
                  <Select.Trigger><Select.ValueText /></Select.Trigger>
                  <Portal>
                    <Select.Positioner>
                      <Select.Content>
                        {LATENCY_TIER_OPTIONS.map((option) => (
                          <Select.Item key={option.value} item={option}>{option.label}</Select.Item>
                        ))}
                      </Select.Content>
                    </Select.Positioner>
                  </Portal>
                </Select.Root>
                <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">值越低"延迟优先"时越优先</Text>
              </Box>
            </SimpleGrid>
          </Box>
        </Collapsible.Content>
      </Collapsible.Root>
    </AppDialog>
  )
}
