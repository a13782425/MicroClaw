import { useEffect, useState } from 'react'
import {
  Box, Flex, Text, Button, Badge, Tabs, SimpleGrid, Spinner,
} from '@chakra-ui/react'
import { Plus } from 'lucide-react'
import {
  listProviders,
  updateProvider,
  deleteProvider,
  setDefaultProvider,
  type ProviderConfig,
  type ModelType,
} from '@/api/gateway'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { toaster } from '@/components/ui/toaster'
import { ChatProviderDialog } from './chat-provider-dialog'
import { EmbeddingDialog } from './embedding-dialog'
import { EmbeddingReindexDialog } from './embedding-reindex-dialog'
import { ChatCard, EmbeddingCard } from './provider-cards'

export default function ModelsPage() {
  const [providers, setProviders] = useState<ProviderConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [activeTab, setActiveTab] = useState<ModelType>('chat')
  const [chatDialogOpen, setChatDialogOpen] = useState(false)
  const [embeddingDialogOpen, setEmbeddingDialogOpen] = useState(false)
  const [reindexDialogOpen, setReindexDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ProviderConfig | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ProviderConfig | null>(null)

  const chatProviders = providers.filter((provider) => provider.modelType === 'chat')
  const embeddingProviders = providers.filter((provider) => provider.modelType === 'embedding')

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

  const handleToggle = async (provider: ProviderConfig, enabled: boolean) => {
    try {
      await updateProvider({ id: provider.id, isEnabled: enabled })
      setProviders((prev) => prev.map((item) => item.id === provider.id ? { ...item, isEnabled: enabled } : item))
      if (provider.modelType === 'embedding' && enabled) {
        setReindexDialogOpen(true)
      }
    } catch (error) {
      toaster.create({ type: 'error', title: '操作失败', description: String(error) })
    }
  }

  const handleDelete = async (provider: ProviderConfig) => {
    try {
      await deleteProvider(provider.id)
      setProviders((prev) => prev.filter((item) => item.id !== provider.id))
      toaster.create({ type: 'success', title: '已删除' })
    } catch (error) {
      toaster.create({ type: 'error', title: '删除失败', description: String(error) })
    } finally {
      setDeleteTarget(null)
    }
  }

  const handleSetDefault = async (provider: ProviderConfig) => {
    try {
      await setDefaultProvider(provider.id)
      setProviders((prev) => prev.map((item) => ({ ...item, isDefault: item.id === provider.id })))
      toaster.create({ type: 'success', title: `已将「${provider.displayName}」设为默认` })
    } catch (error) {
      toaster.create({ type: 'error', title: '操作失败', description: String(error) })
    }
  }

  const openEdit = (provider: ProviderConfig) => {
    setEditing(provider)
    if (provider.modelType === 'embedding') setEmbeddingDialogOpen(true)
    else setChatDialogOpen(true)
  }

  const openAdd = () => {
    setEditing(null)
    if (activeTab === 'embedding') setEmbeddingDialogOpen(true)
    else setChatDialogOpen(true)
  }

  return (
    <Box p="6">
      <Flex align="center" justify="space-between" mb="6">
        <Box>
          <Text fontSize="xl" fontWeight="bold">模型</Text>
          <Text color="var(--mc-text-muted)" fontSize="sm" mt="1">管理 AI 模型提供方，支持聊天模型与嵌入模型</Text>
        </Box>
        <Button colorPalette={activeTab === 'embedding' ? 'purple' : 'blue'} onClick={openAdd} aria-label="添加提供方">
          <Plus size={16} /> 添加提供方
        </Button>
      </Flex>

      <Tabs.Root value={activeTab} onValueChange={(details) => setActiveTab(details.value as ModelType)}>
        <Tabs.List mb="4">
          <Tabs.Trigger value="chat">
            聊天模型
            {chatProviders.length > 0 && <Badge ml="2" size="sm" colorPalette="blue" variant="subtle">{chatProviders.length}</Badge>}
          </Tabs.Trigger>
          <Tabs.Trigger value="embedding">
            嵌入模型
            {embeddingProviders.length > 0 && <Badge ml="2" size="sm" colorPalette="purple" variant="subtle">{embeddingProviders.length}</Badge>}
          </Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="chat">
          {loading ? (
            <Box py="10" textAlign="center"><Spinner /></Box>
          ) : chatProviders.length === 0 ? (
            <Box py="12" textAlign="center" color="var(--mc-text-muted)">暂无聊天模型</Box>
          ) : (
            <SimpleGrid columns={{ base: 1, xl: 2 }} gap="4">
              {chatProviders.map((provider) => (
                <ChatCard
                  key={provider.id}
                  p={provider}
                  onEdit={openEdit}
                  onDelete={setDeleteTarget}
                  onToggle={handleToggle}
                  onSetDefault={handleSetDefault}
                />
              ))}
            </SimpleGrid>
          )}
        </Tabs.Content>

        <Tabs.Content value="embedding">
          {loading ? (
            <Box py="10" textAlign="center"><Spinner /></Box>
          ) : embeddingProviders.length === 0 ? (
            <Box py="12" textAlign="center" color="var(--mc-text-muted)">暂无嵌入模型</Box>
          ) : (
            <SimpleGrid columns={{ base: 1, xl: 2 }} gap="4">
              {embeddingProviders.map((provider) => (
                <EmbeddingCard
                  key={provider.id}
                  p={provider}
                  onEdit={openEdit}
                  onDelete={setDeleteTarget}
                  onToggle={handleToggle}
                />
              ))}
            </SimpleGrid>
          )}
        </Tabs.Content>
      </Tabs.Root>

      <ChatProviderDialog
        open={chatDialogOpen}
        editing={editing?.modelType === 'chat' ? editing : null}
        onClose={() => setChatDialogOpen(false)}
        onSaved={() => { setChatDialogOpen(false); void load() }}
      />

      <EmbeddingDialog
        open={embeddingDialogOpen}
        editing={editing?.modelType === 'embedding' ? editing : null}
        onClose={() => setEmbeddingDialogOpen(false)}
        onSaved={() => { setEmbeddingDialogOpen(false); void load() }}
      />

      <EmbeddingReindexDialog open={reindexDialogOpen} onClose={() => setReindexDialogOpen(false)} />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除提供方"
        description={`确认删除模型「${deleteTarget?.displayName}」？`}
        confirmText="删除"
      />
    </Box>
  )
}
