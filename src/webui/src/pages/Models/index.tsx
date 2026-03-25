import { useEffect, useState } from 'react'
import {
  Box, Flex, Text, Button, Badge, Switch, Spinner,
  Input, Textarea, Select, Portal, createListCollection,
  SimpleGrid, Card, Collapsible, CheckboxCard,
} from '@chakra-ui/react'
import { Plus, Cpu, Link as LinkIcon, Edit, Trash2, ChevronDown } from 'lucide-react'
import {
  listProviders, createProvider, updateProvider, deleteProvider, setDefaultProvider,
  type ProviderConfig, type ProviderCreateRequest, type ProviderUpdateRequest,
  type ProviderProtocol, type ProviderCapabilities,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

// ─── 工具函数 ──────────────────────────────────────────────────────────────────
function protocolLabel(p: ProviderProtocol): string {
  return p === 'openai' ? 'OpenAI / 兼容' : 'Anthropic'
}

const PROTOCOL_OPTIONS = [
  { value: 'openai', label: 'OpenAI / 兼容' },
  { value: 'anthropic', label: 'Anthropic (Claude)' },
]
const protocolCollection = createListCollection({ items: PROTOCOL_OPTIONS })

// ─── 模态 & 能力选项 ──────────────────────────────────────────────────────────
const INPUT_MODALITIES = [
  { key: 'inputImage', label: '图片' },
  { key: 'inputAudio', label: '音频' },
  { key: 'inputVideo', label: '视频' },
  { key: 'inputFile', label: '文件' },
] as const

const OUTPUT_MODALITIES = [
  { key: 'outputImage', label: '图片' },
  { key: 'outputAudio', label: '音频' },
  { key: 'outputVideo', label: '视频' },
] as const

// ─── 默认表单 ─────────────────────────────────────────────────────────────────
function defaultForm() {
  return {
    displayName: '',
    protocol: 'openai' as ProviderProtocol,
    baseUrl: '',
    apiKey: '',
    modelName: '',
    maxOutputTokens: 8192,
    isEnabled: true,
    inputImage: false,
    inputAudio: false,
    inputVideo: false,
    inputFile: false,
    outputImage: false,
    outputAudio: false,
    outputVideo: false,
    supportsFunctionCalling: false,
    supportsResponsesApi: false,
    inputPricePerMToken: '',
    outputPricePerMToken: '',
    cacheInputPricePerMToken: '',
    cacheOutputPricePerMToken: '',
    notes: '',
  }
}

type FormState = ReturnType<typeof defaultForm>

function hasNonDefaultCapabilities(caps: ProviderCapabilities | undefined | null): boolean {
  if (!caps) return false
  return caps.inputImage || caps.inputAudio || caps.inputVideo || caps.inputFile
    || caps.outputImage || caps.outputAudio || caps.outputVideo
    || caps.supportsFunctionCalling || caps.supportsResponsesApi
}

function hasNonDefaultPricing(caps: ProviderCapabilities | undefined | null): boolean {
  if (!caps) return false
  return !!(caps.inputPricePerMToken || caps.outputPricePerMToken
    || caps.cacheInputPricePerMToken || caps.cacheOutputPricePerMToken
    || caps.notes)
}

function buildCapabilities(form: FormState): Partial<ProviderCapabilities> {
  return {
    inputImage: form.inputImage,
    inputAudio: form.inputAudio,
    inputVideo: form.inputVideo,
    inputFile: form.inputFile,
    outputImage: form.outputImage,
    outputAudio: form.outputAudio,
    outputVideo: form.outputVideo,
    supportsFunctionCalling: form.supportsFunctionCalling,
    supportsResponsesApi: form.supportsResponsesApi,
    inputPricePerMToken: parseFloat(form.inputPricePerMToken) || null,
    outputPricePerMToken: parseFloat(form.outputPricePerMToken) || null,
    cacheInputPricePerMToken: parseFloat(form.cacheInputPricePerMToken) || null,
    cacheOutputPricePerMToken: parseFloat(form.cacheOutputPricePerMToken) || null,
    notes: form.notes || null,
  }
}

// ─── 编辑/新建弹窗 ────────────────────────────────────────────────────────────
interface ProviderDialogProps {
  open: boolean
  editing: ProviderConfig | null
  onClose: () => void
  onSaved: () => void
}

function ProviderDialog({ open, editing, onClose, onSaved }: ProviderDialogProps) {
  const [form, setForm] = useState<FormState>(defaultForm())
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
        })
      } else {
        setForm(defaultForm())
      }
    }
  }, [open, editing])

  if (!open) return null

  const set = (k: keyof FormState, v: unknown) => setForm((f) => ({ ...f, [k]: v }))

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
      const capabilities = buildCapabilities(form)
      if (editing) {
        const req: ProviderUpdateRequest = {
          id: editing.id,
          displayName: form.displayName,
          protocol: form.protocol,
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey || undefined,
          modelName: form.modelName,
          maxOutputTokens: form.maxOutputTokens,
          isEnabled: form.isEnabled,
          capabilities,
        }
        await updateProvider(req)
      } else {
        const req: ProviderCreateRequest = {
          displayName: form.displayName,
          protocol: form.protocol,
          baseUrl: form.baseUrl || undefined,
          apiKey: form.apiKey,
          modelName: form.modelName,
          maxOutputTokens: form.maxOutputTokens,
          isEnabled: form.isEnabled,
          capabilities,
        }
        await createProvider(req)
      }
      toaster.create({ type: 'success', title: editing ? '更新成功' : '添加成功' })
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
      title={editing ? '编辑提供方' : '添加提供方'}
      contentProps={{ maxW: '540px' }}
      bodyProps={{ maxH: '90vh', overflowY: 'auto' }}
      footer={(
        <>
          <Button variant="ghost" onClick={onClose}>取消</Button>
          <Button colorPalette="blue" loading={saving} onClick={handleSave}>保存</Button>
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
          onValueChange={(v) => set('protocol', v.value[0] as ProviderProtocol)}
          collection={protocolCollection}
        >
          <Select.Trigger><Select.ValueText /></Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {PROTOCOL_OPTIONS.map((o) => (
                  <Select.Item key={o.value} item={o}>{o.label}</Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">Base URL</Text>
        <Input value={form.baseUrl} onChange={(e) => set('baseUrl', e.target.value)} placeholder="留空使用官方默认端点" />
        <Text fontSize="xs" color="gray.500" mt="1">填写可接入兼容 API 或代理</Text>
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
          min={256} max={131072}
        />
      </Box>

      {/* ─── 能力配置 ─────────────────────────────────────── */}
      <Collapsible.Root defaultOpen={editing ? hasNonDefaultCapabilities(editing.capabilities) : false}>
        <Collapsible.Trigger asChild>
          <Button variant="ghost" size="sm" w="full" justifyContent="space-between" mb="2">
            <Text fontSize="sm" fontWeight="medium">能力配置</Text>
            <ChevronDown size={14} />
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <Box borderWidth="1px" rounded="md" p="3" mb="3">
            <Text fontSize="xs" color="gray.500" mb="2">输入模态</Text>
            <Flex gap="3" flexWrap="wrap" mb="3">
              {INPUT_MODALITIES.map((m) => (
                <CheckboxCard.Root
                  key={m.key}
                  variant="subtle"
                  size="sm"
                  colorPalette="teal"
                  checked={form[m.key as keyof FormState] as boolean}
                  onCheckedChange={(e) => set(m.key as keyof FormState, !!e.checked)}
                >
                  <CheckboxCard.HiddenInput />
                  <CheckboxCard.Control>
                    <CheckboxCard.Indicator />
                    <CheckboxCard.Label>{m.label}</CheckboxCard.Label>
                  </CheckboxCard.Control>
                </CheckboxCard.Root>
              ))}
            </Flex>

            <Text fontSize="xs" color="gray.500" mb="2">输出模态</Text>
            <Flex gap="3" flexWrap="wrap" mb="3">
              {OUTPUT_MODALITIES.map((m) => (
                <CheckboxCard.Root
                  key={m.key}
                  variant="subtle"
                  size="sm"
                  colorPalette="teal"
                  checked={form[m.key as keyof FormState] as boolean}
                  onCheckedChange={(e) => set(m.key as keyof FormState, !!e.checked)}
                >
                  <CheckboxCard.HiddenInput />
                  <CheckboxCard.Control>
                    <CheckboxCard.Indicator />
                    <CheckboxCard.Label>{m.label}</CheckboxCard.Label>
                  </CheckboxCard.Control>
                </CheckboxCard.Root>
              ))}
            </Flex>

            <Text fontSize="xs" color="gray.500" mb="2">特殊能力</Text>
            <Flex gap="4">
              <Switch.Root size="sm" checked={form.supportsFunctionCalling} onCheckedChange={(d) => set('supportsFunctionCalling', d.checked)}>
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
                <Switch.Label fontSize="xs">Function Calling</Switch.Label>
              </Switch.Root>
              <Switch.Root size="sm" checked={form.supportsResponsesApi} onCheckedChange={(d) => set('supportsResponsesApi', d.checked)}>
                <Switch.HiddenInput />
                <Switch.Control><Switch.Thumb /></Switch.Control>
                <Switch.Label fontSize="xs">Responses API</Switch.Label>
              </Switch.Root>
            </Flex>
          </Box>
        </Collapsible.Content>
      </Collapsible.Root>

      {/* ─── 价格 & 备注 ─────────────────────────────────── */}
      <Collapsible.Root defaultOpen={editing ? hasNonDefaultPricing(editing.capabilities) : false}>
        <Collapsible.Trigger asChild>
          <Button variant="ghost" size="sm" w="full" justifyContent="space-between" mb="2">
            <Text fontSize="sm" fontWeight="medium">价格 & 备注</Text>
            <ChevronDown size={14} />
          </Button>
        </Collapsible.Trigger>
        <Collapsible.Content>
          <Box borderWidth="1px" rounded="md" p="3" mb="3">
            <Text fontSize="xs" color="gray.500" mb="2">单价（$ / 1M tokens）</Text>
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
    </AppDialog>
  )
}

// ─── 主组件 ──────────────────────────────────────────────────────────────────
export default function ModelsPage() {
  const [providers, setProviders] = useState<ProviderConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ProviderConfig | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ProviderConfig | null>(null)

  const load = async () => {
    setLoading(true)
    try {
      const data = await listProviders()
      setProviders(data)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const handleToggle = async (p: ProviderConfig, enabled: boolean) => {
    try {
      await updateProvider({ id: p.id, isEnabled: enabled })
      setProviders((prev) => prev.map((x) => x.id === p.id ? { ...x, isEnabled: enabled } : x))
    } catch (err) {
      toaster.create({ type: 'error', title: '操作失败', description: String(err) })
    }
  }

  const handleDelete = async (p: ProviderConfig) => {
    try {
      await deleteProvider(p.id)
      setProviders((prev) => prev.filter((x) => x.id !== p.id))
      toaster.create({ type: 'success', title: '已删除' })
    } catch (err) {
      toaster.create({ type: 'error', title: '删除失败', description: String(err) })
    } finally {
      setDeleteTarget(null)
    }
  }

  const handleSetDefault = async (p: ProviderConfig) => {
    try {
      await setDefaultProvider(p.id)
      setProviders((prev) => prev.map((x) => ({ ...x, isDefault: x.id === p.id })))
      toaster.create({ type: 'success', title: `已将「${p.displayName}」设为默认` })
    } catch (err) {
      toaster.create({ type: 'error', title: '操作失败', description: String(err) })
    }
  }

  return (
    <Box p="6">
      <Flex align="center" justify="space-between" mb="6">
        <Box>
          <Text fontSize="xl" fontWeight="bold">模型</Text>
          <Text color="gray.500" fontSize="sm" mt="1">管理 AI 模型提供方，支持 OpenAI（兼容）和 Anthropic 协议</Text>
        </Box>
        <Button colorPalette="blue" onClick={() => { setEditing(null); setDialogOpen(true) }}>
          <Plus size={16} /> 添加提供方
        </Button>
      </Flex>

      {loading ? (
        <Flex justify="center" py="16"><Spinner /></Flex>
      ) : providers.length === 0 ? (
        <Flex align="center" justify="center" py="16" flexDir="column" gap="3" color="gray.400">
          <Cpu size={48} />
          <Text>暂无提供方</Text>
          <Button colorPalette="blue" onClick={() => { setEditing(null); setDialogOpen(true) }}>
            <Plus size={16} /> 添加提供方
          </Button>
        </Flex>
      ) : (
        <SimpleGrid columns={{ base: 1, md: 2, lg: 3 }} gap="4">
          {providers.map((p) => (
            <Card.Root key={p.id} opacity={p.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline">
              <Card.Body p="4">
                <Flex align="center" gap="2" mb="2">
                  <Text fontWeight="semibold" flex="1" truncate>{p.displayName}</Text>
                  {p.isDefault && <Badge colorPalette="yellow" size="sm">默认</Badge>}
                  <Badge colorPalette={p.protocol === 'openai' ? 'blue' : 'purple'} size="sm">
                    {protocolLabel(p.protocol)}
                  </Badge>
                </Flex>

                <Flex align="center" gap="1" color="gray.600" _dark={{ color: 'gray.400' }} fontSize="sm" mb="1">
                  <Cpu size={13} />
                  <Text>{p.modelName}</Text>
                </Flex>
                <Text fontSize="xs" color="gray.400" mb="1">
                  最大输出 {p.maxOutputTokens.toLocaleString()} tokens
                </Text>

                {p.baseUrl && (
                  <Flex align="center" gap="1" color="gray.500" fontSize="xs" mb="2">
                    <LinkIcon size={11} />
                    <Text truncate>{p.baseUrl}</Text>
                  </Flex>
                )}

                <Flex gap="1" flexWrap="wrap" mb="3">
                  {p.capabilities?.inputImage && <Badge size="sm" colorPalette="cyan" variant="subtle">图片输入</Badge>}
                  {p.capabilities?.inputAudio && <Badge size="sm" colorPalette="teal" variant="subtle">音频输入</Badge>}
                  {p.capabilities?.inputVideo && <Badge size="sm" colorPalette="blue" variant="subtle">视频输入</Badge>}
                  {p.capabilities?.inputFile && <Badge size="sm" colorPalette="gray" variant="subtle">文件</Badge>}
                  {p.capabilities?.outputImage && <Badge size="sm" colorPalette="cyan" variant="outline">图片输出</Badge>}
                  {p.capabilities?.outputAudio && <Badge size="sm" colorPalette="teal" variant="outline">音频输出</Badge>}
                  {p.capabilities?.outputVideo && <Badge size="sm" colorPalette="blue" variant="outline">视频输出</Badge>}
                  {p.capabilities?.supportsFunctionCalling && <Badge size="sm" colorPalette="orange" variant="subtle">Functions</Badge>}
                  {p.capabilities?.supportsResponsesApi && <Badge size="sm" colorPalette="green" variant="subtle">Responses</Badge>}
                  {(p.capabilities?.inputPricePerMToken || p.capabilities?.outputPricePerMToken) && (
                    <Badge size="sm" variant="outline" colorPalette="purple">
                      ${p.capabilities.inputPricePerMToken ?? '?'}/{p.capabilities.outputPricePerMToken ?? '?'}/M
                    </Badge>
                  )}
                </Flex>

                <Flex align="center" justify="space-between">
                  <Switch.Root size="sm" checked={p.isEnabled} onCheckedChange={(d) => handleToggle(p, d.checked)}>
                    <Switch.HiddenInput />
                    <Switch.Control><Switch.Thumb /></Switch.Control>
                    <Switch.Label fontSize="xs">{p.isEnabled ? '启用' : '停用'}</Switch.Label>
                  </Switch.Root>

                  <Flex gap="1">
                    <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => { setEditing(p); setDialogOpen(true) }}>
                      <Edit size={12} /> 编辑
                    </Button>
                    {!p.isDefault && (
                      <Button size="xs" variant="ghost" colorPalette="yellow" onClick={() => handleSetDefault(p)}>
                        设默认
                      </Button>
                    )}
                    <Button size="xs" variant="ghost" colorPalette="red" aria-label={`删除提供方 ${p.displayName}`} onClick={() => setDeleteTarget(p)}>
                      <Trash2 size={12} />
                    </Button>
                  </Flex>
                </Flex>
              </Card.Body>
            </Card.Root>
          ))}
        </SimpleGrid>
      )}

      <ProviderDialog
        open={dialogOpen}
        editing={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={load}
      />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除提供方"
        description={`确认删除提供方「${deleteTarget?.displayName}」？`}
        confirmText="删除"
      />
    </Box>
  )
}
