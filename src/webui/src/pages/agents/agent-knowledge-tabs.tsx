import { useState, useEffect } from 'react'
import {
  Box, Text, Badge, Button, HStack, VStack, Spinner,
} from '@chakra-ui/react'
import { Trash2 } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  listAgentPainMemories,
  deleteAgentPainMemory,
  type AgentConfig,
  type PainMemoryDto,
} from '@/api/gateway'
import {
  SEVERITY_COLORS,
  SEVERITY_LABELS,
} from './agent-constants'

export function SafetyMemoryTab({ agent }: { agent: AgentConfig }) {
  const [memories, setMemories] = useState<PainMemoryDto[]>([])
  const [loading, setLoading] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

  const load = async () => {
    setLoading(true)
    try {
      const data = await listAgentPainMemories(agent.id)
      setMemories(data)
    } catch {
      // no-op
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [agent.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const handleDeleteClick = (memoryId: string) => {
    setPendingDeleteId(memoryId)
    setConfirmOpen(true)
  }

  const handleDeleteConfirm = async () => {
    if (!pendingDeleteId) return
    setDeletingId(pendingDeleteId)
    try {
      await deleteAgentPainMemory(agent.id, pendingDeleteId)
      setMemories((prev) => prev.filter((memory) => memory.id !== pendingDeleteId))
      toaster.create({ title: '已删除痛觉记忆', type: 'success' })
    } catch {
      toaster.create({ title: '删除失败', type: 'error' })
    } finally {
      setDeletingId(null)
      setPendingDeleteId(null)
      setConfirmOpen(false)
    }
  }

  return (
    <Box p="3">
      <HStack mb="3" justify="space-between">
        <Text fontSize="sm" fontWeight="medium">安全记忆</Text>
        <Button size="xs" variant="outline" data-mc-refresh="true" onClick={load} loading={loading}>刷新</Button>
      </HStack>

      {loading && <Spinner size="sm" />}

      {!loading && memories.length === 0 && (
        <Box py="8" textAlign="center">
          <Text fontSize="sm" color="var(--mc-text-muted)">暂无痛觉记忆记录</Text>
          <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">当 Agent 执行操作失败且被记录痛觉时，将在此处显示</Text>
        </Box>
      )}

      {memories.map((memory) => (
        <Box key={memory.id} mb="3" p="3" borderWidth="1px" rounded="md" bg="var(--mc-surface-muted)">
          <HStack justify="space-between" mb="2" flexWrap="wrap" gap="2">
            <HStack gap="2">
              <Badge colorPalette={SEVERITY_COLORS[memory.severity] ?? 'gray'} size="sm">
                {SEVERITY_LABELS[memory.severity] ?? memory.severity}
              </Badge>
              <Badge variant="outline" size="sm">第 {memory.occurrenceCount} 次</Badge>
            </HStack>
            <Button
              size="xs"
              variant="ghost"
              colorPalette="red"
              loading={deletingId === memory.id}
              onClick={() => handleDeleteClick(memory.id)}
            >
              <Trash2 size={12} />
            </Button>
          </HStack>

          <VStack align="start" gap="1">
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">触发点</Text>
              <Text fontSize="sm">{memory.triggerDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">后果</Text>
              <Text fontSize="sm">{memory.consequenceDescription}</Text>
            </Box>
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)">规避策略</Text>
              <Text fontSize="sm" color="var(--mc-link-color)">{memory.avoidanceStrategy}</Text>
            </Box>
            <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">
              最近发生：{new Date(memory.lastOccurredAtMs).toLocaleString('zh-CN')}
            </Text>
          </VStack>
        </Box>
      ))}

      <ConfirmDialog
        open={confirmOpen}
        onClose={() => { setConfirmOpen(false); setPendingDeleteId(null) }}
        onConfirm={handleDeleteConfirm}
        title="删除痛觉记忆"
        description="确认删除该条痛觉记忆？删除后无法恢复。"
        confirmText="删除"
        loading={deletingId !== null}
      />
    </Box>
  )
}


