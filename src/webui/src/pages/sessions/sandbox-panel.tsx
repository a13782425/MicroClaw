import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, IconButton, Spinner, Portal,
} from '@chakra-ui/react'
import { X, RefreshCw, Download, Copy, ChevronRight, ChevronDown, Folder, FileText } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { listSessionSandbox, createSandboxToken, type SandboxNode } from '@/api/gateway'

// ── 格式化工具 ────────────────────────────────────────────────────────────────

function formatSize(bytes: number): string {
  if (bytes === 0) return '0 B'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// ── 右键菜单 ──────────────────────────────────────────────────────────────────

interface CtxMenuProps {
  x: number
  y: number
  onDownload: () => void
  onCopy: () => void
  onClose: () => void
}

function ContextMenu({ x, y, onDownload, onCopy, onClose }: CtxMenuProps) {
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        onClose()
      }
    }
    // 延迟绑定，避免触发菜单的那次 click 立即关闭
    const id = setTimeout(() => window.addEventListener('mousedown', handler), 0)
    return () => {
      clearTimeout(id)
      window.removeEventListener('mousedown', handler)
    }
  }, [onClose])

  return (
    <Portal>
      <Box
        ref={ref}
        position="fixed"
        left={`${x}px`}
        top={`${y}px`}
        bg="white"
       
        boxShadow="md"
        rounded="md"
        borderWidth="1px"
        borderColor="var(--mc-border)"
        zIndex={9999}
        py="1"
        minW="140px"
        fontSize="sm"
      >
        <Flex
          px="3"
          py="1.5"
          gap="2"
          align="center"
          cursor="pointer"
          _hover={{ bg: 'gray.100', _dark: { bg: 'gray.700' } }}
          onClick={() => { onClose(); onDownload() }}
        >
          <Download size={13} />
          <Text>下载</Text>
        </Flex>
        <Flex
          px="3"
          py="1.5"
          gap="2"
          align="center"
          cursor="pointer"
          _hover={{ bg: 'gray.100', _dark: { bg: 'gray.700' } }}
          onClick={() => { onClose(); onCopy() }}
        >
          <Copy size={13} />
          <Text>复制下载链接</Text>
        </Flex>
      </Box>
    </Portal>
  )
}

// ── 树节点组件 ────────────────────────────────────────────────────────────────

function TreeNode({
  node,
  sessionId,
  depth,
}: {
  node: SandboxNode
  sessionId: string
  depth: number
}) {
  const [expanded, setExpanded] = useState(true)
  const [loadingAction, setLoadingAction] = useState<'download' | 'copy' | null>(null)
  const [ctxPos, setCtxPos] = useState<{ x: number; y: number } | null>(null)

  const getDownloadUrl = useCallback(async (): Promise<string | null> => {
    // 优先使用内嵌的 downloadUrl（文件列表时由后端直接生成，节省一次请求）
    if (node.downloadUrl) {
      return window.location.origin + node.downloadUrl
    }
    try {
      const result = await createSandboxToken(sessionId, node.relativePath)
      return window.location.origin + result.downloadUrl
    } catch {
      return null
    }
  }, [node, sessionId])

  const handleDownload = async () => {
    setLoadingAction('download')
    try {
      const url = await getDownloadUrl()
      if (!url) {
        toaster.create({ type: 'error', title: '获取下载链接失败' })
        return
      }
      window.open(url, '_blank')
    } finally {
      setLoadingAction(null)
    }
  }

  const handleCopy = async () => {
    setLoadingAction('copy')
    try {
      const url = await getDownloadUrl()
      if (!url) {
        toaster.create({ type: 'error', title: '获取下载链接失败' })
        return
      }
      await navigator.clipboard.writeText(url)
      toaster.create({ type: 'success', title: '链接已复制' })
    } finally {
      setLoadingAction(null)
    }
  }

  const handleContextMenu = (e: React.MouseEvent) => {
    e.preventDefault()
    setCtxPos({ x: e.clientX, y: e.clientY })
  }

  const paddingLeft = `${8 + depth * 16}px`

  if (node.isDirectory) {
    return (
      <>
        <Flex
          align="center"
          px="2"
          py="1"
          gap="1"
          cursor="pointer"
          _hover={{ bg: 'gray.50', _dark: { bg: 'gray.800' } }}
          onClick={() => setExpanded(e => !e)}
          style={{ paddingLeft }}
        >
          {expanded
            ? <ChevronDown size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
            : <ChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />}
          <Folder size={14} style={{ flexShrink: 0, color: '#d97706' }} />
          <Text fontSize="xs" fontWeight="medium" truncate flex="1">{node.name}</Text>
        </Flex>
        {expanded && node.children?.map(child => (
          <TreeNode key={child.relativePath} node={child} sessionId={sessionId} depth={depth + 1} />
        ))}
      </>
    )
  }

  return (
    <>
      <Flex
        align="center"
        px="2"
        py="1"
        gap="1"
        role="group"
        _hover={{ bg: 'gray.50', _dark: { bg: 'gray.800' } }}
        onContextMenu={handleContextMenu}
        style={{ paddingLeft }}
      >
        <FileText size={14} style={{ flexShrink: 0, opacity: 0.4 }} />
        <Box flex="1" minW={0}>
          <Text fontSize="xs" truncate title={node.relativePath}>{node.name}</Text>
          <Text fontSize="xs" color="var(--mc-text-muted)">{formatSize(node.size)}</Text>
        </Box>
        <Flex gap="1" opacity={0} _groupHover={{ opacity: 1 }} transition="opacity 0.15s">
          <IconButton
            aria-label="下载"
            title="下载"
            size="xs"
            variant="ghost"
            loading={loadingAction === 'download'}
            onClick={handleDownload}
          >
            <Download size={12} />
          </IconButton>
          <IconButton
            aria-label="复制下载链接"
            title="复制下载链接"
            size="xs"
            variant="ghost"
            loading={loadingAction === 'copy'}
            onClick={handleCopy}
          >
            <Copy size={12} />
          </IconButton>
        </Flex>
      </Flex>
      {ctxPos && (
        <ContextMenu
          x={ctxPos.x}
          y={ctxPos.y}
          onDownload={handleDownload}
          onCopy={handleCopy}
          onClose={() => setCtxPos(null)}
        />
      )}
    </>
  )
}

// ── 主面板 ────────────────────────────────────────────────────────────────────

export interface SandboxPanelProps {
  sessionId: string
  onClose: () => void
}

export default function SandboxPanel({ sessionId, onClose }: SandboxPanelProps) {
  const [nodes, setNodes] = useState<SandboxNode[]>([])
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listSessionSandbox(sessionId)
      setNodes(data)
    } catch {
      toaster.create({ type: 'error', title: '加载沙盒文件失败' })
    } finally {
      setLoading(false)
    }
  }, [sessionId])

  useEffect(() => {
    if (sessionId) load()
  }, [sessionId, load])

  const totalFiles = countFiles(nodes)

  return (
    <Flex
      direction="column"
      w="240px"
      flexShrink={0}
      borderLeftWidth="1px"
      h="100%"
      overflow="hidden"
    >
      {/* 头部 */}
      <Flex
        align="center"
        px="3"
        py="2"
        borderBottomWidth="1px"
        gap="1"
      >
        <Text fontSize="xs" fontWeight="semibold" flex="1" color="var(--mc-text-muted)">
          沙盒文件{totalFiles > 0 ? ` (${totalFiles})` : ''}
        </Text>
        <IconButton aria-label="刷新" title="刷新" size="xs" variant="ghost" onClick={load} loading={loading}>
          <RefreshCw size={13} />
        </IconButton>
        <IconButton aria-label="关闭" title="关闭" size="xs" variant="ghost" onClick={onClose}>
          <X size={13} />
        </IconButton>
      </Flex>

      {/* 内容 */}
      <Box flex="1" overflowY="auto">
        {loading && nodes.length === 0 ? (
          <Flex justify="center" align="center" h="60px">
            <Spinner size="sm" />
          </Flex>
        ) : nodes.length === 0 ? (
          <Box p="4" textAlign="center">
            <Text fontSize="xs" color="var(--mc-text-muted)">沙盒为空</Text>
          </Box>
        ) : (
          nodes.map(node => (
            <TreeNode key={node.relativePath} node={node} sessionId={sessionId} depth={0} />
          ))
        )}
      </Box>
    </Flex>
  )
}

function countFiles(nodes: SandboxNode[]): number {
  let count = 0
  for (const node of nodes) {
    if (!node.isDirectory) count++
    if (node.children) count += countFiles(node.children)
  }
  return count
}
