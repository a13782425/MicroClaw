import { useState, useEffect, useRef, useCallback } from 'react'
import {
  Box, Text, Badge, Tabs, Table, Button, HStack, Spinner, VStack,
  NativeSelect, Card, SimpleGrid, Separator, Progress, Input,
} from '@chakra-ui/react'
import { Upload, Trash2, RefreshCw, FileText, MessageSquare, Clock, BarChart2, Settings, Database } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  listRagGlobalDocuments,
  uploadRagGlobalDocument,
  deleteRagGlobalDocument,
  reindexRagGlobalDocument,
  listSessions,
  getSessionRagStatus,
  vectorizeSessionMessages,
  getRagQueryStats,
  getRagConfig,
  updateRagConfig,
  type RagDocumentInfo,
  type SessionInfo,
  type SessionRagStatus,
  type RagQueryStats,
  type RagConfig,
} from '@/api/gateway'

function formatDate(ms: number): string {
  if (!ms) return '—'
  return new Date(ms).toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
  })
}

// ─── 全局知识库 Tab ────────────────────────────────────────────────────────────

function GlobalKnowledgeTab() {
  const [docs, setDocs] = useState<RagDocumentInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [actionTarget, setActionTarget] = useState<RagDocumentInfo | null>(null)
  const [confirmType, setConfirmType] = useState<'delete' | 'reindex' | null>(null)
  const [actionLoading, setActionLoading] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const fetchDocs = useCallback(async () => {
    setLoading(true)
    try {
      const list = await listRagGlobalDocuments()
      setDocs(list)
    } catch {
      toaster.create({ type: 'error', title: '加载文档列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchDocs() }, [fetchDocs])

  const handleUploadClick = () => fileInputRef.current?.click()

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    e.target.value = ''

    const ext = file.name.split('.').pop()?.toLowerCase()
    if (ext !== 'txt' && ext !== 'md') {
      toaster.create({ type: 'error', title: '仅支持 .txt 和 .md 格式的文档' })
      return
    }

    setUploading(true)
    try {
      const result = await uploadRagGlobalDocument(file)
      toaster.create({ type: 'success', title: `上传成功，共 ${result.chunkCount} 个分块` })
      await fetchDocs()
    } catch {
      toaster.create({ type: 'error', title: '上传失败' })
    } finally {
      setUploading(false)
    }
  }

  const openConfirm = (doc: RagDocumentInfo, type: 'delete' | 'reindex') => {
    setActionTarget(doc)
    setConfirmType(type)
  }

  const closeConfirm = () => {
    setActionTarget(null)
    setConfirmType(null)
  }

  const handleConfirm = async () => {
    if (!actionTarget || !confirmType) return
    setActionLoading(true)
    try {
      if (confirmType === 'delete') {
        await deleteRagGlobalDocument(actionTarget.sourceId)
        toaster.create({ type: 'success', title: `已删除「${actionTarget.fileName}」` })
      } else {
        const result = await reindexRagGlobalDocument(actionTarget.sourceId)
        toaster.create({ type: 'success', title: `重索引完成，共 ${result.chunkCount} 个分块` })
      }
      closeConfirm()
      await fetchDocs()
    } catch {
      toaster.create({ type: 'error', title: confirmType === 'delete' ? '删除失败' : '重索引失败' })
    } finally {
      setActionLoading(false)
    }
  }

  return (
    <Box>
      <HStack justify="space-between" mb="4">
        <Text fontSize="sm" color="gray.500">
          已上传 {docs.length} 个文档，分块后嵌入向量库，供 Agent 语义检索使用。
        </Text>
        <HStack>
          <Button size="sm" variant="outline" onClick={fetchDocs} loading={loading}>
            <RefreshCw size={14} />
            刷新
          </Button>
          <Button size="sm" colorPalette="blue" onClick={handleUploadClick} loading={uploading}>
            <Upload size={14} />
            上传文档
          </Button>
          <input
            ref={fileInputRef}
            type="file"
            accept=".txt,.md"
            style={{ display: 'none' }}
            onChange={handleFileChange}
          />
        </HStack>
      </HStack>

      {loading && docs.length === 0 ? (
        <Box py="12" textAlign="center">
          <Spinner size="lg" color="blue.500" />
          <Text mt="3" color="gray.500">加载中…</Text>
        </Box>
      ) : docs.length === 0 ? (
        <Box py="16" textAlign="center" border="1px dashed" borderColor="gray.200" borderRadius="lg">
          <FileText size={40} color="var(--chakra-colors-gray-300)" style={{ margin: '0 auto 12px' }} />
          <Text color="gray.500" fontWeight="medium">暂无已索引文档</Text>
          <Text fontSize="sm" color="gray.400" mt="1">
            点击「上传文档」添加 .txt 或 .md 文件
          </Text>
        </Box>
      ) : (
        <Table.Root size="sm" variant="outline" borderRadius="md" overflow="hidden">
          <Table.Header>
            <Table.Row bg="gray.50">
              <Table.ColumnHeader>文件名</Table.ColumnHeader>
              <Table.ColumnHeader width="100px" textAlign="center">分块数</Table.ColumnHeader>
              <Table.ColumnHeader width="180px">索引时间</Table.ColumnHeader>
              <Table.ColumnHeader width="160px" textAlign="right">操作</Table.ColumnHeader>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {docs.map((doc) => (
              <Table.Row key={doc.sourceId}>
                <Table.Cell>
                  <HStack gap="2">
                    <FileText size={14} color="var(--chakra-colors-blue-500)" />
                    <Text fontWeight="medium" fontSize="sm">{doc.fileName}</Text>
                    <Badge size="sm" colorPalette="green" variant="subtle">已索引</Badge>
                  </HStack>
                </Table.Cell>
                <Table.Cell textAlign="center">
                  <Text fontSize="sm">{doc.chunkCount}</Text>
                </Table.Cell>
                <Table.Cell>
                  <Text fontSize="sm" color="gray.500">{formatDate(doc.indexedAtMs)}</Text>
                </Table.Cell>
                <Table.Cell textAlign="right">
                  <HStack gap="1" justify="flex-end">
                    <Button
                      size="xs"
                      variant="ghost"
                      colorPalette="blue"
                      onClick={() => openConfirm(doc, 'reindex')}
                    >
                      <RefreshCw size={12} />
                      重索引
                    </Button>
                    <Button
                      size="xs"
                      variant="ghost"
                      colorPalette="red"
                      onClick={() => openConfirm(doc, 'delete')}
                    >
                      <Trash2 size={12} />
                      删除
                    </Button>
                  </HStack>
                </Table.Cell>
              </Table.Row>
            ))}
          </Table.Body>
        </Table.Root>
      )}

      <ConfirmDialog
        open={confirmType === 'delete' && actionTarget !== null}
        title="确认删除"
        description={`确定要删除文档「${actionTarget?.fileName}」吗？此操作将同时移除所有向量分块，且不可撤销。`}
        confirmText="删除"
        colorPalette="red"
        loading={actionLoading}
        onConfirm={handleConfirm}
        onClose={closeConfirm}
      />

      <ConfirmDialog
        open={confirmType === 'reindex' && actionTarget !== null}
        title="确认重索引"
        description={`确定要对文档「${actionTarget?.fileName}」进行重索引吗？将重新生成所有向量嵌入，适合切换嵌入模型后使用。`}
        confirmText="重索引"
        colorPalette="blue"
        loading={actionLoading}
        onConfirm={handleConfirm}
        onClose={closeConfirm}
      />
    </Box>
  )
}

// ─── 会话知识库 Tab ───────────────────────────────────────────────────────────

function SessionKnowledgeTab() {
  const [sessions, setSessions] = useState<SessionInfo[]>([])
  const [selectedSessionId, setSelectedSessionId] = useState<string>('')
  const [sessionsLoading, setSessionsLoading] = useState(false)
  const [status, setStatus] = useState<SessionRagStatus | null>(null)
  const [statusLoading, setStatusLoading] = useState(false)
  const [vectorizing, setVectorizing] = useState(false)

  // 加载会话列表
  useEffect(() => {
    setSessionsLoading(true)
    listSessions()
      .then((data) => {
        setSessions(data)
        if (data.length > 0) setSelectedSessionId(data[0].id)
      })
      .catch(() => toaster.create({ type: 'error', title: '加载会话列表失败' }))
      .finally(() => setSessionsLoading(false))
  }, [])

  // 选择会话后加载 RAG 状态
  const loadStatus = useCallback(async (sessionId: string) => {
    if (!sessionId) return
    setStatusLoading(true)
    try {
      const s = await getSessionRagStatus(sessionId)
      setStatus(s)
    } catch {
      toaster.create({ type: 'error', title: '加载索引状态失败' })
    } finally {
      setStatusLoading(false)
    }
  }, [])

  useEffect(() => {
    if (selectedSessionId) loadStatus(selectedSessionId)
    else setStatus(null)
  }, [selectedSessionId, loadStatus])

  const selectedSession = sessions.find((s) => s.id === selectedSessionId)

  return (
    <Box>
      {/* 会话选择器 */}
      <HStack mb="5" gap="3" align="center">
        <Text fontSize="sm" fontWeight="medium" whiteSpace="nowrap">选择会话：</Text>
        {sessionsLoading ? (
          <Spinner size="sm" />
        ) : sessions.length === 0 ? (
          <Text fontSize="sm" color="gray.400">暂无会话</Text>
        ) : (
          <NativeSelect.Root size="sm" maxW="400px" flex="1">
            <NativeSelect.Field
              value={selectedSessionId}
              onChange={(e) => setSelectedSessionId(e.target.value)}
            >
              {sessions.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.title} ({s.channelType})
                </option>
              ))}
            </NativeSelect.Field>
            <NativeSelect.Indicator />
          </NativeSelect.Root>
        )}
        <Button
          size="sm"
          variant="outline"
          onClick={() => selectedSessionId && loadStatus(selectedSessionId)}
          loading={statusLoading}
          disabled={!selectedSessionId}
        >
          <RefreshCw size={14} />
          刷新
        </Button>
        <Button
          size="sm"
          variant="outline"
          colorPalette="blue"
          onClick={async () => {
            if (!selectedSessionId) return
            setVectorizing(true)
            try {
              const result = await vectorizeSessionMessages(selectedSessionId)
              toaster.create({ type: 'success', title: `已将 ${result.messageCount} 条消息写入待处理队列` })
              loadStatus(selectedSessionId)
            } catch {
              toaster.create({ type: 'error', title: '手动向量化失败' })
            } finally {
              setVectorizing(false)
            }
          }}
          loading={vectorizing}
          disabled={!selectedSessionId}
        >
          <Database size={14} />
          手动向量化
        </Button>
      </HStack>

      <Separator mb="5" />

      {/* 状态区域 */}
      {!selectedSessionId ? (
        <Box py="16" textAlign="center" border="1px dashed" borderColor="gray.200" borderRadius="lg">
          <MessageSquare size={40} color="var(--chakra-colors-gray-300)" style={{ margin: '0 auto 12px' }} />
          <Text color="gray.500" fontWeight="medium">请先选择一个会话</Text>
          <Text fontSize="sm" color="gray.400" mt="1">选择会话后可查看其 RAG 索引状态</Text>
        </Box>
      ) : statusLoading ? (
        <Box py="12" textAlign="center">
          <Spinner size="lg" color="blue.500" />
          <Text mt="3" color="gray.500">加载中…</Text>
        </Box>
      ) : status ? (
        <Box>
          <Box mb="4">
            <Text fontSize="sm" fontWeight="semibold">{selectedSession?.title ?? selectedSessionId}</Text>
            <Text fontSize="xs" color="gray.400">
              渠道：{selectedSession?.channelType} · ID：{selectedSessionId.slice(0, 8)}…
            </Text>
          </Box>

          <SimpleGrid columns={2} gap="4">
            <Card.Root variant="outline">
              <Card.Body py="4" px="5">
                <HStack gap="3">
                  <Box color="blue.500"><MessageSquare size={22} /></Box>
                  <Box>
                    <Text fontSize="2xl" fontWeight="bold" lineHeight="1">
                      {status.categoryCount}
                    </Text>
                    <Text fontSize="xs" color="gray.500" mt="0.5">已建立分类数</Text>
                  </Box>
                </HStack>
              </Card.Body>
            </Card.Root>

            <Card.Root variant="outline">
              <Card.Body py="4" px="5">
                <HStack gap="3">
                  <Box color="green.500"><Clock size={22} /></Box>
                  <Box>
                    <Text fontSize="sm" fontWeight="semibold" lineHeight="1.2">
                      {status.lastUpdatedAtMs
                        ? new Date(status.lastUpdatedAtMs).toLocaleString('zh-CN', {
                            year: 'numeric', month: '2-digit', day: '2-digit',
                            hour: '2-digit', minute: '2-digit',
                          })
                        : '—'}
                    </Text>
                    <Text fontSize="xs" color="gray.500" mt="0.5">最近更新时间</Text>
                  </Box>
                </HStack>
              </Card.Body>
            </Card.Root>
          </SimpleGrid>

          {status.categoryCount === 0 && (
            <Box mt="4" p="4" bg="orange.50" borderRadius="md" border="1px solid" borderColor="orange.200">
              <Text fontSize="sm" color="orange.700">
                该会话尚无已索引的消息。对话结束后系统会自动索引。
              </Text>
            </Box>
          )}
        </Box>
      ) : null}

    </Box>
  )
}

// ─── 检索统计 Tab ─────────────────────────────────────────────────────────────

type ScopeFilter = 'All' | 'Global' | 'Session'

function StatCard({ label, value, sub }: { label: string; value: string | number; sub?: string }) {
  return (
    <Card.Root variant="outline">
      <Card.Body py="4" px="5">
        <Text fontSize="2xl" fontWeight="bold" lineHeight="1.2">{value}</Text>
        <Text fontSize="sm" fontWeight="semibold" mt="1">{label}</Text>
        {sub && <Text fontSize="xs" color="gray.400" mt="0.5">{sub}</Text>}
      </Card.Body>
    </Card.Root>
  )
}

function RagStatsTab() {
  const [scopeFilter, setScopeFilter] = useState<ScopeFilter>('All')
  const [stats, setStats] = useState<RagQueryStats | null>(null)
  const [loading, setLoading] = useState(false)

  const fetchStats = useCallback(async () => {
    setLoading(true)
    try {
      const result = await getRagQueryStats(scopeFilter === 'All' ? undefined : scopeFilter)
      setStats(result)
    } catch {
      toaster.create({ type: 'error', title: '加载统计数据失败' })
    } finally {
      setLoading(false)
    }
  }, [scopeFilter])

  useEffect(() => { fetchStats() }, [fetchStats])

  const hitRatePct = stats ? Math.round(stats.hitRate * 100) : 0

  return (
    <Box>
      <HStack mb="5" gap="3" justify="space-between">
        <HStack gap="3">
          <Text fontSize="sm" fontWeight="medium" whiteSpace="nowrap">作用域：</Text>
          <NativeSelect.Root size="sm" maxW="160px">
            <NativeSelect.Field
              value={scopeFilter}
              onChange={(e) => setScopeFilter(e.target.value as ScopeFilter)}
            >
              <option value="All">全部</option>
              <option value="Global">全局库</option>
              <option value="Session">会话库</option>
            </NativeSelect.Field>
            <NativeSelect.Indicator />
          </NativeSelect.Root>
        </HStack>
        <Button size="sm" variant="outline" onClick={fetchStats} loading={loading}>
          <RefreshCw size={14} />
          刷新
        </Button>
      </HStack>

      {loading && !stats ? (
        <Box py="16" textAlign="center">
          <Spinner size="lg" color="blue.500" />
        </Box>
      ) : stats ? (
        <Box>
          {/* 概览卡片 */}
          <SimpleGrid columns={{ base: 2, md: 4 }} gap="4" mb="6">
            <StatCard label="总查询次数" value={stats.totalQueries} sub="历史累计" />
            <StatCard label="近 24h 查询" value={stats.last24hQueries} sub="最近活跃度" />
            <StatCard label="平均延迟" value={`${stats.avgElapsedMs} ms`} sub="混合检索耗时" />
            <StatCard label="平均召回数" value={stats.avgRecallCount} sub="每次返回结果" />
          </SimpleGrid>

          {/* 命中率进度条 */}
          <Card.Root variant="outline" mb="4">
            <Card.Body py="5" px="6">
              <HStack justify="space-between" mb="3">
                <Box>
                  <Text fontWeight="semibold">命中率</Text>
                  <Text fontSize="xs" color="gray.500" mt="0.5">
                    召回结果 &gt; 0 的查询占比（{stats.hitQueries} / {stats.totalQueries}）
                  </Text>
                </Box>
                <Text fontSize="2xl" fontWeight="bold" color={hitRatePct >= 80 ? 'green.500' : hitRatePct >= 50 ? 'orange.500' : 'red.500'}>
                  {hitRatePct}%
                </Text>
              </HStack>
              <Progress.Root value={hitRatePct} size="lg" colorPalette={hitRatePct >= 80 ? 'green' : hitRatePct >= 50 ? 'orange' : 'red'}>
                <Progress.Track borderRadius="full">
                  <Progress.Range borderRadius="full" />
                </Progress.Track>
              </Progress.Root>
            </Card.Body>
          </Card.Root>

          {stats.totalQueries === 0 && (
            <Box p="4" bg="blue.50" borderRadius="md" border="1px solid" borderColor="blue.200">
              <Text fontSize="sm" color="blue.700">
                暂无检索记录。Agent 进行 RAG 检索后，统计数据将在此处显示。
              </Text>
            </Box>
          )}
        </Box>
      ) : null}
    </Box>
  )
}

// ─── 设置 Tab ────────────────────────────────────────────────────────────────

function RagSettingsTab() {
  const [config, setConfig] = useState<RagConfig | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [maxStorageSizeMb, setMaxStorageSizeMb] = useState('')
  const [pruneTargetPercent, setPruneTargetPercent] = useState('')

  const fetchConfig = useCallback(async () => {
    setLoading(true)
    try {
      const data = await getRagConfig()
      setConfig(data)
      setMaxStorageSizeMb(String(data.maxStorageSizeMb))
      setPruneTargetPercent(String(Math.round(data.pruneTargetPercent * 100)))
    } catch {
      toaster.create({ type: 'error', title: '加载 RAG 配置失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchConfig() }, [fetchConfig])

  const handleSave = async () => {
    const sizeMb = Number(maxStorageSizeMb)
    const prunePct = Number(pruneTargetPercent) / 100

    if (isNaN(sizeMb) || sizeMb <= 0) {
      toaster.create({ type: 'error', title: '最大存储大小必须大于 0' })
      return
    }
    if (isNaN(prunePct) || prunePct <= 0 || prunePct > 100) {
      toaster.create({ type: 'error', title: '清理目标比例必须在 1-100 之间' })
      return
    }

    setSaving(true)
    try {
      await updateRagConfig({ maxStorageSizeMb: sizeMb, pruneTargetPercent: prunePct })
      setConfig({ maxStorageSizeMb: sizeMb, pruneTargetPercent: prunePct })
      toaster.create({ type: 'success', title: 'RAG 配置已更新' })
    } catch {
      toaster.create({ type: 'error', title: '保存配置失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading && !config) {
    return (
      <Box py="12" textAlign="center">
        <Spinner size="lg" color="blue.500" />
        <Text mt="3" color="gray.500">加载中…</Text>
      </Box>
    )
  }

  return (
    <Box maxW="480px">
      <Text fontSize="sm" color="gray.500" mb="5">
        配置 RAG 自动遗忘机制。当单个会话 RAG 存储超过阈值时，系统将自动删除调用次数最低的向量分块以释放空间。
      </Text>

      <VStack gap="5" align="stretch">
        <Box>
          <Text fontSize="sm" fontWeight="medium" mb="1">最大存储大小 (MB)</Text>
          <Input
            size="sm"
            type="number"
            value={maxStorageSizeMb}
            onChange={(e) => setMaxStorageSizeMb(e.target.value)}
            placeholder="50"
          />
          <Text fontSize="xs" color="gray.400" mt="1">
            单个会话 RAG 数据库文件的最大大小，超过后触发自动清理
          </Text>
        </Box>

        <Box>
          <Text fontSize="sm" fontWeight="medium" mb="1">清理目标比例 (%)</Text>
          <Input
            size="sm"
            type="number"
            value={pruneTargetPercent}
            onChange={(e) => setPruneTargetPercent(e.target.value)}
            placeholder="80"
          />
          <Text fontSize="xs" color="gray.400" mt="1">
            清理后的目标大小占阈值的百分比（例如 80 表示清理到阈值的 80%）
          </Text>
        </Box>

        <Button size="sm" colorPalette="blue" onClick={handleSave} loading={saving} alignSelf="flex-start">
          保存配置
        </Button>
      </VStack>
    </Box>
  )
}

// ─── 页面入口 ─────────────────────────────────────────────────────────────────

export default function RagPage() {
  return (
    <Box p="6">
      <HStack mb="6" gap="3" align="center">
        <Text fontSize="xl" fontWeight="bold">RAG 知识库</Text>
        <Badge colorPalette="blue" variant="subtle">Beta</Badge>
      </HStack>

      <Tabs.Root defaultValue="global" variant="enclosed">
        <Tabs.List mb="4">
          <Tabs.Trigger value="global">🌐 全局知识库</Tabs.Trigger>
          <Tabs.Trigger value="session">💬 会话知识库</Tabs.Trigger>
          <Tabs.Trigger value="stats">📊 检索统计</Tabs.Trigger>
          <Tabs.Trigger value="settings">⚙️ 设置</Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="global">
          <GlobalKnowledgeTab />
        </Tabs.Content>

        <Tabs.Content value="session">
          <SessionKnowledgeTab />
        </Tabs.Content>

        <Tabs.Content value="stats">
          <RagStatsTab />
        </Tabs.Content>

        <Tabs.Content value="settings">
          <RagSettingsTab />
        </Tabs.Content>
      </Tabs.Root>
    </Box>
  )
}

