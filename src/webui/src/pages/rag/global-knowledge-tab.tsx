import { useState, useEffect, useRef, useCallback } from 'react'
import {
  Box, Text, Badge, Table, Button, HStack, Spinner,
} from '@chakra-ui/react'
import { Upload, Trash2, RefreshCw, FileText } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  listRagGlobalDocuments,
  uploadRagGlobalDocument,
  deleteRagGlobalDocument,
  reindexRagGlobalDocument,
  type RagDocumentInfo,
} from '@/api/gateway'
import { formatDate } from './rag-utils'

export function GlobalKnowledgeTab() {
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
        <Text fontSize="sm" color="var(--mc-text-muted)">
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
          <Spinner size="lg" color="var(--mc-info)" />
          <Text mt="3" color="var(--mc-text-muted)">加载中…</Text>
        </Box>
      ) : docs.length === 0 ? (
        <Box py="16" textAlign="center" border="1px dashed" borderColor="var(--mc-border)" borderRadius="lg">
          <FileText size={40} color="var(--chakra-colors-gray-300)" style={{ margin: '0 auto 12px' }} />
          <Text color="var(--mc-text-muted)" fontWeight="medium">暂无已索引文档</Text>
          <Text fontSize="sm" color="var(--mc-text-muted)" mt="1">
            点击「上传文档」添加 .txt 或 .md 文件
          </Text>
        </Box>
      ) : (
        <Table.Root size="sm" variant="outline" borderRadius="md" overflow="hidden">
          <Table.Header>
            <Table.Row bg="var(--mc-surface-muted)">
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
                  <Text fontSize="sm" color="var(--mc-text-muted)">{formatDate(doc.indexedAtMs)}</Text>
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
