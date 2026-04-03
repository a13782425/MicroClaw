import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Input, Spinner, Textarea,
  HStack, Table, IconButton,
} from '@chakra-ui/react'
import { Check, Trash2, RefreshCw } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import {
  getSessionMemory,
  listSessionRagChunks,
  deleteRagChunk,
  updateRagChunkHitCount,
  type SessionInfo,
  type RagChunkInfo,
} from '@/api/gateway'

export function SessionMemoryTab({ session }: { session: SessionInfo }) {
  const [memoryContent, setMemoryContent] = useState('')
  const [chunks, setChunks] = useState<RagChunkInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [editingHitCount, setEditingHitCount] = useState<Record<string, number>>({})

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [memory, chunkList] = await Promise.all([
        getSessionMemory(session.id),
        listSessionRagChunks(session.id),
      ])
      setMemoryContent(memory)
      setChunks(chunkList)
    } catch {
      toaster.create({ type: 'error', title: '加载记忆数据失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id])

  useEffect(() => { loadData() }, [loadData])

  const handleDeleteChunk = async (chunk: RagChunkInfo) => {
    try {
      await deleteRagChunk(chunk.id, 'Session', session.id)
      setChunks((prev) => prev.filter((item) => item.id !== chunk.id))
      toaster.create({ type: 'success', title: 'Chunk 已删除' })
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    }
  }

  const handleUpdateHitCount = async (chunk: RagChunkInfo) => {
    const newCount = editingHitCount[chunk.id]
    if (newCount === undefined || newCount === chunk.hitCount) return
    try {
      await updateRagChunkHitCount(chunk.id, newCount, 'Session', session.id)
      setChunks((prev) =>
        prev.map((item) => (item.id === chunk.id ? { ...item, hitCount: newCount } : item)),
      )
      setEditingHitCount((prev) => {
        const next = { ...prev }
        delete next[chunk.id]
        return next
      })
      toaster.create({ type: 'success', title: '命中次数已更新' })
    } catch {
      toaster.create({ type: 'error', title: '更新失败' })
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Flex direction="column" h="100%" p="3" gap="3" overflow="auto">
      <Box>
        <HStack justify="space-between" mb="1">
          <Text fontSize="xs" color="var(--mc-text-muted)">长期记忆（MEMORY.md）— 只读，由 AI 自动维护</Text>
          <IconButton aria-label="刷新" size="xs" variant="ghost" data-mc-refresh="true" onClick={loadData}>
            <RefreshCw size={14} />
          </IconButton>
        </HStack>
        <Textarea
          fontFamily="mono"
          fontSize="sm"
          resize="vertical"
          minH="100px"
          maxH="200px"
          readOnly
          value={memoryContent}
          spellCheck={false}
          bg="var(--mc-surface-muted)"
         
        />
      </Box>

      <Box flex="1" overflow="auto">
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">
          RAG 知识片段（{chunks.length} 个）— 可删除或修改命中次数
        </Text>
        {chunks.length === 0 ? (
          <Text fontSize="sm" color="var(--mc-text-muted)" p="4" textAlign="center">暂无 RAG 知识片段</Text>
        ) : (
          <Table.Root size="sm" variant="outline">
            <Table.Header>
              <Table.Row>
                <Table.ColumnHeader w="40%">内容</Table.ColumnHeader>
                <Table.ColumnHeader w="15%">来源</Table.ColumnHeader>
                <Table.ColumnHeader w="15%">命中次数</Table.ColumnHeader>
                <Table.ColumnHeader w="15%">创建时间</Table.ColumnHeader>
                <Table.ColumnHeader w="15%">操作</Table.ColumnHeader>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {chunks.map((chunk) => (
                <Table.Row key={chunk.id}>
                  <Table.Cell>
                    <Text fontSize="xs" lineClamp={3} title={chunk.content}>
                      {chunk.content}
                    </Text>
                  </Table.Cell>
                  <Table.Cell>
                    <Text fontSize="xs" color="var(--mc-text-muted)">{chunk.sourceId}</Text>
                  </Table.Cell>
                  <Table.Cell>
                    <HStack gap="1">
                      <Input
                        size="xs"
                        type="number"
                        w="60px"
                        min={0}
                        value={editingHitCount[chunk.id] ?? chunk.hitCount}
                        onChange={(e) =>
                          setEditingHitCount((prev) => ({
                            ...prev,
                            [chunk.id]: parseInt(e.target.value, 10) || 0,
                          }))
                        }
                      />
                      {editingHitCount[chunk.id] !== undefined &&
                        editingHitCount[chunk.id] !== chunk.hitCount && (
                          <IconButton
                            aria-label="保存命中次数"
                            size="xs"
                            variant="ghost"
                            colorPalette="green"
                            onClick={() => handleUpdateHitCount(chunk)}
                          >
                            <Check size={12} />
                          </IconButton>
                        )}
                    </HStack>
                  </Table.Cell>
                  <Table.Cell>
                    <Text fontSize="xs" color="var(--mc-text-muted)">
                      {new Date(chunk.createdAtMs).toLocaleDateString()}
                    </Text>
                  </Table.Cell>
                  <Table.Cell>
                    <IconButton
                      aria-label="删除"
                      size="xs"
                      variant="ghost"
                      colorPalette="red"
                      onClick={() => handleDeleteChunk(chunk)}
                    >
                      <Trash2 size={14} />
                    </IconButton>
                  </Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table.Root>
        )}
      </Box>
    </Flex>
  )
}
