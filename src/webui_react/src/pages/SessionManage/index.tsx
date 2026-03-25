import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Input, Spinner, Tabs, Textarea,
  Button, HStack, VStack, For, Em,
} from '@chakra-ui/react'
import { RefreshCw, Check, Ban } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { eventBus } from '@/services/eventBus'
import {
  listSessions, approveSession, disableSession,
  listSessionDna, updateSessionDna,
  getSessionMemory, updateSessionMemory,
  importSessionDnaFromFeishu,
  type SessionInfo, type SessionDnaFileInfo,
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
            <Text color="gray.500" fontSize="sm">暂无会话</Text>
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
                _dark={{ bg: isActive ? 'blue.900' : undefined }}
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
  if (files.length === 0) return <Box p="4"><Text color="gray.500" fontSize="sm">暂无 DNA 文件</Text></Box>

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
            <Text fontSize="xs" color="gray.500">{currentFile.description}</Text>
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
  const [content, setContent] = useState('')
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setLoading(true)
    getSessionMemory(session.id)
      .then(setContent)
      .catch(() => toaster.create({ type: 'error', title: '加载记忆失败' }))
      .finally(() => setLoading(false))
  }, [session.id])

  const save = async () => {
    setSaving(true)
    try {
      await updateSessionMemory(session.id, content)
      toaster.create({ type: 'success', title: '记忆已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Flex direction="column" h="100%" p="3" gap="2">
      <Text fontSize="xs" color="gray.500">长期记忆（MEMORY.md）始终注入 System Prompt</Text>
      <Textarea
        flex="1"
        fontFamily="mono"
        fontSize="sm"
        resize="none"
        placeholder="此 Session 的长期记忆（Markdown 格式）..."
        value={content}
        onChange={(e) => setContent(e.target.value)}
        spellCheck={false}
      />
      <Box>
        <Button size="sm" colorPalette="blue" loading={saving} onClick={save}>
          保存
        </Button>
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
        <Text fontSize="sm" color="gray.600">当前状态：</Text>
        {session.isApproved
          ? <Badge colorPalette="green" size="md">已批准</Badge>
          : <Badge colorPalette="orange" size="md">待审批</Badge>
        }
      </HStack>
      {session.approvalReason && (
        <Box>
          <Text fontSize="xs" color="gray.500">审批原因：{session.approvalReason}</Text>
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
            <Em color="gray.400">从左侧选择一个会话</Em>
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
