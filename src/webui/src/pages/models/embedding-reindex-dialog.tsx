import { useEffect, useRef, useState } from 'react'
import { Flex, Text, Button, Spinner, Box } from '@chakra-ui/react'
import {
  startRagReindexAll,
  getRagReindexStatus,
  type RagReindexStatus,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { toaster } from '@/components/ui/toaster'

export function EmbeddingReindexDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [phase, setPhase] = useState<'confirm' | 'progress'>('confirm')
  const [status, setStatus] = useState<RagReindexStatus | null>(null)
  const [starting, setStarting] = useState(false)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const clearPolling = () => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }
  }

  const startPolling = () => {
    clearPolling()
    intervalRef.current = setInterval(async () => {
      try {
        const nextStatus = await getRagReindexStatus()
        setStatus(nextStatus)
        if (nextStatus.status === 'done' || nextStatus.status === 'error') clearPolling()
      } catch {
        // ignore poll errors
      }
    }, 1500)
  }

  useEffect(() => {
    if (open) {
      setPhase('confirm')
      setStatus(null)
      setStarting(false)
    } else {
      clearPolling()
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(() => () => clearPolling(), [])

  const handleStart = async () => {
    setStarting(true)
    setPhase('progress')
    try {
      await startRagReindexAll()
      const nextStatus = await getRagReindexStatus()
      setStatus(nextStatus)
      startPolling()
    } catch (error) {
      toaster.create({ type: 'error', title: '启动失败', description: String(error) })
      setPhase('confirm')
    } finally {
      setStarting(false)
    }
  }

  const jobStatus = status?.status ?? 'idle'
  const isRunning = phase === 'progress' && (jobStatus === 'running' || starting)
  const isDone = jobStatus === 'done'
  const isError = jobStatus === 'error'
  const handleClose = isRunning ? () => {} : onClose

  if (phase === 'confirm') {
    return (
      <AppDialog
        open={open}
        onClose={onClose}
        title="嵌入模型已更换 — 知识库重索引"
        footer={(
          <>
            <Button variant="ghost" onClick={onClose}>跳过</Button>
            <Button colorPalette="purple" loading={starting} onClick={handleStart}>
              开始重索引
            </Button>
          </>
        )}
      >
        <Text color="gray.500" fontSize="sm" mb="4">
          切换嵌入模型后，旧向量维度与新模型不兼容。建议立即对所有知识库进行重索引以确保搜索正常。
        </Text>
        <Text color="gray.400" fontSize="sm">点击「开始重索引」以自动处理全局文档与所有会话知识库。</Text>
      </AppDialog>
    )
  }

  return (
    <AppDialog
      open={open}
      onClose={handleClose}
      title="知识库重索引"
      footer={!isRunning ? (
        <Button variant="ghost" onClick={onClose}>
          {isDone ? '完成' : '关闭'}
        </Button>
      ) : undefined}
    >
      <Text color="gray.500" fontSize="sm" mb="4">
        切换嵌入模型后，旧向量维度与新模型不兼容。建议立即对所有知识库进行重索引以确保搜索正常。
      </Text>
      {isRunning && (
        <Flex align="center" gap="3">
          <Spinner size="sm" color="purple.400" />
          <Box>
            <Text fontSize="sm" fontWeight="medium">正在重索引中…</Text>
            {status && (
              <Text fontSize="xs" color="gray.500" mt="1">
                已完成 {status.completed} / {status.total}
                {status.currentItem && `（当前：${status.currentItem}）`}
              </Text>
            )}
          </Box>
        </Flex>
      )}
      {isDone && (
        <Text color="green.400" fontSize="sm" fontWeight="medium">
          重索引完成！共处理 {status?.total ?? 0} 个项目。
        </Text>
      )}
      {isError && (
        <Text color="red.400" fontSize="sm">
          重索引失败：{status?.error ?? '未知错误'}
        </Text>
      )}
    </AppDialog>
  )
}
