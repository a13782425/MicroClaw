import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Input, Spinner, Tabs, Textarea,
  Button, HStack, VStack, For, Em, Table, IconButton,
} from '@chakra-ui/react'
import { RefreshCw, Check, Ban, Trash2, RotateCcw } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { eventBus } from '@/services/eventBus'
import {
  listSessions, approveSession, disableSession,
  listSessionDna, updateSessionDna,
  getSessionMemory,
  listSessionRagChunks, deleteRagChunk, updateRagChunkHitCount,
  importSessionDnaFromFeishu,
  type SessionInfo, type SessionDnaFileInfo, type RagChunkInfo,
} from '@/api/gateway'

// ──────────────────────────────── 左侧列表 ────────────────────────────────────

function SessionList({
  sessions,
  selected,
  onSelect,
}: {
  sessions: SessionInfo[]
  selected: SessionInfo | null
  onSelect: (s: SessionInfo) => void
}) {
  const [query, setQuery] = useState('')
  const filtered = sessions.filter((s) =>
    s.title.toLowerCase().includes(query.toLowerCase()),
  )
  return (
    <Flex direction="column" h="100%" overflow="hidden">
      <Box p="3" borderBottomWidth="1px">
        <Input
          size="sm"
          placeholder="搜索会话..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </Box>
      <Box flex="1" overflowY="auto">
        {filtered.length === 0 && (
          <Box p="6" textAlign="center">
            <Text color="var(--mc-text-muted)" fontSize="sm">暂无会话</Text>
          </Box>
        )}
        <For each={filtered}>
          {(s) => {
            const isActive = selected?.id === s.id
            return (
              <Box
                key={s.id}
                px="3" py="2"
                cursor="pointer"
                borderBottomWidth="1px"
                bg={isActive ? 'blue.50' : undefined}
               
                _hover={{ bg: isActive ? 'blue.50' : 'gray.50', _dark: { bg: isActive ? 'blue.900' : 'gray.800' } }}
                onClick={() => onSelect(s)}
              >
                <Text fontSize="sm" fontWeight="medium" truncate>{s.title}</Text>
                <HStack mt="1" gap="1" flexWrap="wrap">
                  <Badge size="xs" colorPalette="gray" variant="outline">{s.channelType}</Badge>
                  {s.isApproved
                    ? <Badge size="xs" colorPalette="green">已批准</Badge>
                    : <Badge size="xs" colorPalette="orange">待审批</Badge>
                  }
                </HStack>
              </Box>
            )
          }}
        </For>
      </Box>
    </Flex>
  )
}

// ─────────────────── DNA Tab ─────────────────────────────────────────────────

function DnaTab({ session }: { session: SessionInfo }) {
  const [files, setFiles] = useState<SessionDnaFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [activeFile, setActiveFile] = useState<string | null>(null)
  const [feishuUrl, setFeishuUrl] = useState('')
  const [importing, setImporting] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listSessionDna(session.id)
      setFiles(data)
      const init: Record<string, string> = {}
      data.forEach((f) => { init[f.fileName] = f.content })
      setEdits(init)
      if (data.length > 0 && !activeFile) setActiveFile(data[0].fileName)
    } catch {
      toaster.create({ type: 'error', title: '加载 DNA 文件失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id, activeFile])

  useEffect(() => { load() }, [session.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const save = async (fileName: string) => {
    setSaving(true)
    try {
      await updateSessionDna(session.id, fileName, edits[fileName] ?? '')
      toaster.create({ type: 'success', title: '保存成功' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  const importFeishu = async () => {
    if (!feishuUrl.trim() || !activeFile) return
    setImporting(true)
    try {
      const res = await importSessionDnaFromFeishu(session.id, feishuUrl.trim(), activeFile)
      toaster.create({ type: 'success', title: `导入成功，共 ${res.charCount} 字` })
      setEdits((prev) => ({ ...prev, [activeFile]: res.file.content }))
      setFeishuUrl('')
    } catch {
      toaster.create({ type: 'error', title: '飞书导入失败' })
    } finally {
      setImporting(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>
  if (files.length === 0) return <Box p="4"><Text color="var(--mc-text-muted)" fontSize="sm">暂无 DNA 文件</Text></Box>

  const currentFile = files.find((f) => f.fileName === activeFile)

  return (
    <Flex h="100%" direction="column">
      {/* 文件选项卡 */}
      <HStack gap="1" px="3" pt="3" flexWrap="wrap">
        {files.map((f) => (
          <Button
            key={f.fileName}
            size="xs"
            variant={activeFile === f.fileName ? 'solid' : 'outline'}
            colorPalette="blue"
            onClick={() => setActiveFile(f.fileName)}
          >
            {f.fileName.replace('.md', '')}
          </Button>
        ))}
      </HStack>

      {currentFile && (
        <Flex direction="column" flex="1" p="3" gap="2" overflow="hidden">
          {currentFile.description && (
            <Text fontSize="xs" color="var(--mc-text-muted)">{currentFile.description}</Text>
          )}
          <Textarea
            flex="1"
            fontFamily="mono"
            fontSize="sm"
            resize="none"
            value={edits[currentFile.fileName] ?? ''}
            onChange={(e) => setEdits((prev) => ({ ...prev, [currentFile.fileName]: e.target.value }))}
            spellCheck={false}
          />
          <HStack>
            <Button size="sm" colorPalette="blue" loading={saving} onClick={() => save(currentFile.fileName)}>
              保存
            </Button>
            {session.channelType === 'feishu' && (
              <>
                <Input
                  size="sm"
                  flex="1"
                  placeholder="飞书文档 URL 或 Token"
                  value={feishuUrl}
                  onChange={(e) => setFeishuUrl(e.target.value)}
                />
                <Button size="sm" variant="outline" loading={importing} onClick={importFeishu}>
                  飞书导入
                </Button>
              </>
            )}
          </HStack>
        </Flex>
      )}
    </Flex>
  )
}

// ─────────────────── Memory Tab ──────────────────────────────────────────────

function MemoryTab({ session }: { session: SessionInfo }) {
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
      setChunks((prev) => prev.filter((c) => c.id !== chunk.id))
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
        prev.map((c) => (c.id === chunk.id ? { ...c, hitCount: newCount } : c)),
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
      {/* 长期记忆（只读） */}
      <Box>
        <HStack justify="space-between" mb="1">
          <Text fontSize="xs" color="var(--mc-text-muted)">长期记忆（MEMORY.md）— 只读，由 AI 自动维护</Text>
          <IconButton aria-label="刷新" size="xs" variant="ghost" onClick={loadData}>
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

      {/* RAG Chunks 管理 */}
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

// ─────────────────── Approval Tab ────────────────────────────────────────────

function ApprovalTab({
  session,
  onUpdated,
}: {
  session: SessionInfo
  onUpdated: (s: SessionInfo) => void
}) {
  const [loading, setLoading] = useState(false)

  const approve = async () => {
    setLoading(true)
    try {
      const updated = await approveSession(session.id)
      onUpdated(updated)
      toaster.create({ type: 'success', title: '会话已批准' })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setLoading(false)
    }
  }

  const disable = async () => {
    setLoading(true)
    try {
      const updated = await disableSession(session.id)
      onUpdated(updated)
      toaster.create({ type: 'success', title: '会话已禁用' })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <VStack align="start" p="4" gap="4">
      <HStack>
        <Text fontSize="sm" color="var(--mc-text-muted)">当前状态：</Text>
        {session.isApproved
          ? <Badge colorPalette="green" size="md">已批准</Badge>
          : <Badge colorPalette="orange" size="md">待审批</Badge>
        }
      </HStack>
      {session.approvalReason && (
        <Box>
          <Text fontSize="xs" color="var(--mc-text-muted)">审批原因：{session.approvalReason}</Text>
        </Box>
      )}
      <HStack>
        {!session.isApproved ? (
          <Button
            size="sm"
            colorPalette="green"
            loading={loading}
            onClick={approve}
          >
            <Check size={14} />
            批准此会话
          </Button>
        ) : (
          <Button
            size="sm"
            colorPalette="orange"
            variant="outline"
            loading={loading}
            onClick={disable}
          >
            <Ban size={14} />
            禁用此会话
          </Button>
        )}
      </HStack>
    </VStack>
  )
}

// ──────────────────────────── 主页面 ─────────────────────────────────────────

export default function SessionManagePage() {
  const [sessions, setSessions] = useState<SessionInfo[]>([])
  const [listLoading, setListLoading] = useState(false)
  const [selected, setSelected] = useState<SessionInfo | null>(null)
  const loadedRef = useRef(false)

  const load = useCallback(async (showLoading = true) => {
    if (showLoading) setListLoading(true)
    try {
      const data = await listSessions()
      setSessions(data)
      // 更新已选中的 session 状态
      setSelected((prev) => prev ? (data.find((s) => s.id === prev.id) ?? prev) : null)
    } catch {
      toaster.create({ type: 'error', title: '加载会话列表失败' })
    } finally {
      setListLoading(false)
    }
  }, [])

  useEffect(() => {
    if (!loadedRef.current) {
      loadedRef.current = true
      load()
    }
  }, [load])

  useEffect(() => {
    const refresh = () => {
      load(false)
      toaster.create({ type: 'info', title: '有新的待审批会话' })
    }
    const refreshSilent = () => load(false)
    eventBus.on('session:pendingApproval', refresh)
    eventBus.on('session:approved', refreshSilent)
    eventBus.on('session:disabled', refreshSilent)
    return () => {
      eventBus.off('session:pendingApproval', refresh)
      eventBus.off('session:approved', refreshSilent)
      eventBus.off('session:disabled', refreshSilent)
    }
  }, [load])

  const handleUpdated = (updated: SessionInfo) => {
    setSessions((prev) => prev.map((s) => (s.id === updated.id ? updated : s)))
    setSelected(updated)
  }

  return (
    <Flex h="100%" overflow="hidden">
      {/* 左侧 */}
      <Flex
        direction="column"
        w="300px"
        minW="300px"
        borderRightWidth="1px"
        overflow="hidden"
      >
        <HStack px="3" py="2" borderBottomWidth="1px" justify="space-between">
          <Text fontWeight="semibold" fontSize="sm">会话管理</Text>
          <Button size="xs" variant="ghost" loading={listLoading} onClick={() => load()}>
            <RefreshCw size={14} />
          </Button>
        </HStack>
        {listLoading && sessions.length === 0
          ? <Box p="6" textAlign="center"><Spinner /></Box>
          : <SessionList sessions={sessions} selected={selected} onSelect={setSelected} />
        }
      </Flex>

      {/* 右侧 */}
      <Flex flex="1" direction="column" overflow="hidden">
        {!selected ? (
          <Flex flex="1" align="center" justify="center">
            <Em color="var(--mc-text-muted)">从左侧选择一个会话</Em>
          </Flex>
        ) : (
          <>
            <HStack px="4" py="3" borderBottomWidth="1px">
              <Text fontWeight="semibold" truncate flex="1">{selected.title}</Text>
              {selected.isApproved
                ? <Badge colorPalette="green" size="sm">已批准</Badge>
                : <Badge colorPalette="orange" size="sm">待审批</Badge>
              }
            </HStack>
            <Tabs.Root defaultValue="dna" flex="1" display="flex" flexDirection="column" overflow="hidden">
              <Tabs.List px="3">
                <Tabs.Trigger value="dna">🧬 DNA</Tabs.Trigger>
                <Tabs.Trigger value="memory">🧠 记忆</Tabs.Trigger>
                <Tabs.Trigger value="approval">✅ 审批</Tabs.Trigger>
              </Tabs.List>
              <Tabs.Content value="dna" flex="1" overflow="hidden" p="0">
                <DnaTab session={selected} />
              </Tabs.Content>
              <Tabs.Content value="memory" flex="1" overflow="hidden" p="0">
                <MemoryTab session={selected} />
              </Tabs.Content>
              <Tabs.Content value="approval" p="0">
                <ApprovalTab session={selected} onUpdated={handleUpdated} />
              </Tabs.Content>
            </Tabs.Root>
          </>
        )}
      </Flex>
    </Flex>
  )
}
