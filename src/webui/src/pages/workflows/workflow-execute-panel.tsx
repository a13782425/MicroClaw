/**
 * WorkflowExecutePanel — SSE 执行面板，实时显示节点进度和最终输出。
 */
import { useState, useRef } from 'react'
import {
  Box, VStack, HStack, Text, Button, Textarea, Badge, Spinner,
} from '@chakra-ui/react'
import { Play, Square, RotateCcw } from 'lucide-react'
import { useWorkflowStore, type NodeExecutionState } from '@/store/workflowStore'
import type { WorkflowConfig } from '@/api/gateway'

const STATUS_COLOR: Record<string, string> = {
  idle: 'gray',
  running: 'yellow',
  completed: 'green',
  error: 'red',
}

const STATUS_LABEL: Record<string, string> = {
  idle: '等待',
  running: '执行中',
  completed: '完成',
  error: '出错',
}

function NodeStatusRow({ nodeId, label, state }: {
  nodeId: string
  label: string
  state: NodeExecutionState | undefined
}) {
  const status = state?.status ?? 'idle'
  return (
    <HStack gap="2" px="3" py="1.5" borderRadius="md" bg="var(--mc-surface-muted)" align="start">
      <Badge colorPalette={STATUS_COLOR[status]} minW="60px" textAlign="center" flexShrink={0}>
        {STATUS_LABEL[status]}
      </Badge>
      {status === 'running' && <Spinner size="xs" color="var(--mc-warning)" flexShrink={0} />}
      <VStack gap="0.5" align="start" flex={1}>
        <Text fontSize="sm" fontWeight="medium" color="var(--mc-text)">{label}</Text>
        <Text fontSize="xs" color="var(--mc-text-muted)" fontFamily="mono">{nodeId}</Text>
        {state?.warning && (
          <Text fontSize="xs" color="var(--mc-warning)">{state.warning}</Text>
        )}
        {state?.error && (
          <Text fontSize="xs" color="var(--mc-danger)">{state.error}</Text>
        )}
        {state?.result && status === 'completed' && (
          <Text fontSize="xs" color="var(--mc-text-muted)" overflow="hidden" style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical' }}>{state.result}</Text>
        )}
      </VStack>
      {(state?.durationMs !== undefined) && (
        <Text fontSize="xs" color="var(--mc-text-muted)" flexShrink={0}>{state.durationMs}ms</Text>
      )}
    </HStack>
  )
}

interface WorkflowExecutePanelProps {
  workflow: WorkflowConfig
}

export function WorkflowExecutePanel({ workflow }: WorkflowExecutePanelProps) {
  const [input, setInput] = useState('')
  const abortRef = useRef<AbortController | null>(null)

  const {
    executionRunning,
    executionOutput,
    nodeStates,
    error,
    executeWorkflow,
    resetExecution,
  } = useWorkflowStore()

  const handleRun = () => {
    if (!input.trim()) return
    abortRef.current = executeWorkflow(workflow.id, input.trim())
  }

  const handleStop = () => {
    abortRef.current?.abort()
    abortRef.current = null
  }

  const handleReset = () => {
    abortRef.current?.abort()
    abortRef.current = null
    resetExecution()
    setInput('')
  }

  const hasStarted = Object.keys(nodeStates).length > 0 || executionOutput !== '' || error !== null

  return (
    <VStack gap="3" align="stretch" h="100%">
      {/* 输入区 */}
      <Box>
        <Text fontSize="sm" fontWeight="medium" color="var(--mc-text)" mb="1.5">输入消息</Text>
        <Textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="给工作流发送的初始消息..."
          rows={3}
          bg="var(--mc-surface-muted)"
         
          borderColor="var(--mc-border)"
          color="var(--mc-text)"
          resize="none"
          disabled={executionRunning}
          _placeholder={{ color: 'gray.400' }}
        />
      </Box>

      {/* 操作按钮 */}
      <HStack gap="2">
        <Button
          colorPalette="blue"
          size="sm"
          onClick={handleRun}
          disabled={executionRunning || !input.trim()}
          flex={1}
        >
          <Play size={14} />
          执行
        </Button>
        {executionRunning && (
          <Button size="sm" variant="outline" colorPalette="red" onClick={handleStop}>
            <Square size={14} />
            停止
          </Button>
        )}
        {hasStarted && !executionRunning && (
          <Button size="sm" variant="ghost" onClick={handleReset}>
            <RotateCcw size={14} />
            重置
          </Button>
        )}
      </HStack>

      {/* 节点进度 */}
      {workflow.nodes.length > 0 && (
        <Box>
          <Text fontSize="sm" fontWeight="medium" color="var(--mc-text)" mb="1.5">节点状态</Text>
          <VStack gap="1.5" align="stretch">
            {workflow.nodes.map((n) => (
              <NodeStatusRow
                key={n.nodeId}
                nodeId={n.nodeId}
                label={n.label}
                state={nodeStates[n.nodeId]}
              />
            ))}
          </VStack>
        </Box>
      )}

      {/* 最终输出 */}
      {executionOutput && (
        <Box flex={1}>
          <Text fontSize="sm" fontWeight="medium" color="var(--mc-text)" mb="1.5">执行输出</Text>
          <Box
            bg="var(--mc-surface-muted)"
           
            borderRadius="md"
            p="3"
            maxH="200px"
            overflowY="auto"
            fontFamily="mono"
            fontSize="sm"
            color="var(--mc-text)"
            whiteSpace="pre-wrap"
          >
            {executionOutput}
          </Box>
        </Box>
      )}

      {/* 错误提示 */}
      {error && (
        <Box bg="var(--mc-danger-soft)" borderRadius="md" p="3">
          <Text fontSize="sm" color="var(--mc-danger)">{error}</Text>
        </Box>
      )}
    </VStack>
  )
}
