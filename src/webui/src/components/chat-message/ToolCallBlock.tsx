import { useState } from 'react'
import { Box, Text, Flex, Badge } from '@chakra-ui/react'
import { ChevronDown, ChevronRight, Wrench, Check, X, Loader } from 'lucide-react'
import type { SessionMessage } from '@/api/gateway'

interface ToolCallBlockProps {
  message: SessionMessage
  /** 对应的 tool_result 消息（如已收到）*/
  resultMessage?: SessionMessage | null
}

export default function ToolCallBlock({ message, resultMessage }: ToolCallBlockProps) {
  const [argsOpen, setArgsOpen] = useState(false)
  const [resultOpen, setResultOpen] = useState(false)

  const meta = message.metadata ?? {}
  const toolName = (meta.toolName as string) ?? '未知工具'
  const args = meta.arguments as Record<string, unknown> | undefined

  const resultMeta = resultMessage?.metadata ?? {}
  const success = resultMeta.success as boolean | undefined
  const durationMs = resultMeta.durationMs as number | undefined
  const hasResult = resultMessage != null

  return (
    <Box
      borderWidth="1px"
      borderRadius="md"
      borderColor="var(--mc-tool-call-border)"
      overflow="hidden"
      mb="2"
      maxW="80%"
      css={!hasResult ? {
        animation: 'pulse-border 2s ease-in-out infinite',
        '@keyframes pulse-border': {
          '0%, 100%': { borderColor: 'var(--mc-tool-call-border)' },
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
        bg="var(--mc-tool-call-bg)"
        color="var(--mc-text)"
      >
        <Wrench size={14} />
        <Text fontSize="xs" fontWeight="medium" flex="1">
          {toolName}
        </Text>
        {hasResult ? (
          success ? (
            <Badge colorPalette="green" size="sm">
              <Check size={10} /> {durationMs}ms
            </Badge>
          ) : (
            <Badge colorPalette="red" size="sm">
              <X size={10} /> 失败
            </Badge>
          )
        ) : (
          <Loader size={14} className="animate-spin" />
        )}
      </Flex>

      {/* Arguments (collapsible) */}
      {args && Object.keys(args).length > 0 && (
        <>
          <Flex
            align="center"
            gap="1"
            px="3"
            py="1"
            cursor="pointer"
            onClick={() => setArgsOpen(!argsOpen)}
            userSelect="none"
            borderTopWidth="1px"
            borderColor="var(--mc-tool-call-border)"
          >
            {argsOpen ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            <Text fontSize="xs" color="var(--mc-text-muted)">
              参数
            </Text>
          </Flex>
          {argsOpen && (
            <Box
              px="3"
              py="2"
              fontSize="xs"
              fontFamily="mono"
              bg="var(--mc-card)"
              color="var(--mc-text)"
              whiteSpace="pre-wrap"
              overflowX="auto"
            >
              {JSON.stringify(args, null, 2)}
            </Box>
          )}
        </>
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
            borderColor="var(--mc-tool-call-border)"
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
              maxH="300px"
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
