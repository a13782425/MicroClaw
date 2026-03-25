import { useEffect, useState } from 'react'
import {
  Box, Flex, Text, Button, Badge, Switch, Spinner,
  Input, Select, Portal, createListCollection,
  SimpleGrid, Card,
} from '@chakra-ui/react'
import { Plus, Cpu, Link as LinkIcon, Edit, Trash2 } from 'lucide-react'
import {
  listProviders, createProvider, updateProvider, deleteProvider, setDefaultProvider,
  type ProviderConfig, type ProviderCreateRequest, type ProviderUpdateRequest,
  type ProviderProtocol,
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
  }
}

type FormState = ReturnType<typeof defaultForm>

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
        setForm({
          displayName: editing.displayName,
          protocol: editing.protocol,
          baseUrl: editing.baseUrl ?? '',
          apiKey: '',
          modelName: editing.modelName,
          maxOutputTokens: editing.maxOutputTokens,
          isEnabled: editing.isEnabled,
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
      contentProps={{ maxW: '480px' }}
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

      <Box>
        <Text fontSize="sm" mb="1" fontWeight="medium">最大输出 Tokens</Text>
        <Input
          type="number"
          value={form.maxOutputTokens}
          onChange={(e) => set('maxOutputTokens', parseInt(e.target.value) || 8192)}
          min={256} max={131072}
        />
      </Box>
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
                  {p.capabilities?.inputImage && <Badge size="sm" colorPalette="cyan" variant="subtle">图片</Badge>}
                  {p.capabilities?.inputAudio && <Badge size="sm" colorPalette="teal" variant="subtle">音频</Badge>}
                  {p.capabilities?.inputFile && <Badge size="sm" colorPalette="gray" variant="subtle">文件</Badge>}
                  {p.capabilities?.supportsFunctionCalling && <Badge size="sm" colorPalette="orange" variant="subtle">Functions</Badge>}
                  {p.capabilities?.supportsResponsesApi && <Badge size="sm" colorPalette="green" variant="subtle">Responses</Badge>}
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
