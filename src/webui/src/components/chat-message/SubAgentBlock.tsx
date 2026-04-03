import { useState, useEffect, useRef } from 'react'
import { Box, Text, Flex, Badge } from '@chakra-ui/react'
import { Bot, Check, Loader, ChevronDown, ChevronRight } from 'lucide-react'
import type { SessionMessage } from '@/api/gateway'

interface SubAgentBlockProps {
  message: SessionMessage
  /** 对应的 sub_agent_result 消息（如已收到）*/
  resultMessage?: SessionMessage | null
  /** 子代理执行过程中的进度步骤 */
  progressSteps?: string[]
}

/** 实时计时器：显示已用秒数 */
function ElapsedTimer() {
  const [elapsed, setElapsed] = useState(0)
  const startTime = useRef(Date.now())

  useEffect(() => {
    const timer = setInterval(() => {
      setElapsed(Math.floor((Date.now() - startTime.current) / 1000))
    }, 1000)
    return () => clearInterval(timer)
  }, [])

  return (
    <Text fontSize="xs" color="var(--mc-text-muted)" fontFamily="mono">
      {elapsed}s
    </Text>
  )
}

export default function SubAgentBlock({ message, resultMessage, progressSteps }: SubAgentBlockProps) {
  const [resultOpen, setResultOpen] = useState(false)

  const meta = message.metadata ?? {}
  const agentName = (meta.agentName as string) ?? '子代理'
  const task = meta.task as string | undefined

  const resultMeta = resultMessage?.metadata ?? {}
  const durationMs = resultMeta.durationMs as number | undefined
  const hasResult = resultMessage != null

  return (
    <Box
      borderWidth="1px"
      borderRadius="md"
      borderColor="var(--mc-sub-agent-border)"
      overflow="hidden"
      mb="2"
      maxW="80%"
      css={!hasResult ? {
        animation: 'pulse-border-sub 2s ease-in-out infinite',
        '@keyframes pulse-border-sub': {
          '0%, 100%': { borderColor: 'var(--mc-sub-agent-border)' },
          '50%': { opacity: 0.6 },
        },
      } : undefined}
    >
      {/* Header */}
      <Flex
        align="center"
        gap="2"
        px="3"
        py="1.5"
        bg="var(--mc-sub-agent-bg)"
        color="var(--mc-text)"
      >
        <Bot size={14} />
        <Text fontSize="xs" fontWeight="medium" flex="1">
          {agentName}
        </Text>
        {hasResult ? (
          <Badge colorPalette="green" size="sm">
            <Check size={10} /> {durationMs}ms
          </Badge>
        ) : (
          <Flex align="center" gap="2">
            <ElapsedTimer />
            <Loader size={14} className="animate-spin" />
          </Flex>
        )}
      </Flex>

      {/* Task description */}
      {task && (
        <Box
          px="3"
          py="2"
          fontSize="xs"
          color="var(--mc-text-muted)"
          borderTopWidth="1px"
          borderColor="var(--mc-sub-agent-border)"
        >
          {task}
        </Box>
      )}

      {/* Progress steps (live) */}
      {!hasResult && progressSteps && progressSteps.length > 0 && (
        <Box
          px="3"
          py="1.5"
          fontSize="xs"
          color="var(--mc-text-muted)"
          borderTopWidth="1px"
          borderColor="var(--mc-sub-agent-border)"
          maxH="120px"
          overflowY="auto"
        >
          {progressSteps.map((step, i) => (
            <Text key={i} fontSize="xs" fontFamily="mono" lineHeight="1.6">
              → {step}
            </Text>
          ))}
        </Box>
      )}

      {/* Result (collapsible) */}
      {hasResult && resultMessage?.content && (
        <>
          <Flex
            align="center"
            gap="1"
            px="3"
            py="1"
            cursor="pointer"
            onClick={() => setResultOpen(!resultOpen)}
            userSelect="none"
            borderTopWidth="1px"
            borderColor="var(--mc-sub-agent-border)"
          >
            {resultOpen ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            <Text fontSize="xs" color="var(--mc-text-muted)">
              结果
            </Text>
          </Flex>
          {resultOpen && (
            <Box
              px="3"
              py="2"
              fontSize="xs"
              fontFamily="mono"
              bg="var(--mc-card)"
              color="var(--mc-text)"
              whiteSpace="pre-wrap"
              overflowX="auto"
              maxH="200px"
              overflowY="auto"
            >
              {resultMessage.content}
            </Box>
          )}
        </>
      )}
    </Box>
  )
}
