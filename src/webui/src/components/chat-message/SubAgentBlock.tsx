import { Box, Text, Flex, Badge } from '@chakra-ui/react'
import { Bot, Check, Loader } from 'lucide-react'
import type { SessionMessage } from '@/api/gateway'

interface SubAgentBlockProps {
  message: SessionMessage
  /** 对应的 sub_agent_result 消息（如已收到）*/
  resultMessage?: SessionMessage | null
}

export default function SubAgentBlock({ message, resultMessage }: SubAgentBlockProps) {
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
      borderColor="teal.200"
      _dark={{ borderColor: 'teal.700' }}
      overflow="hidden"
      mb="2"
      maxW="80%"
    >
      {/* Header */}
      <Flex
        align="center"
        gap="2"
        px="3"
        py="1.5"
        bg="teal.50"
        _dark={{ bg: 'teal.900' }}
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
          <Loader size={12} className="animate-spin" />
        )}
      </Flex>

      {/* Task description */}
      {task && (
        <Box
          px="3"
          py="2"
          fontSize="xs"
          color="gray.600"
          _dark={{ color: 'gray.400', borderColor: 'teal.800' }}
          borderTopWidth="1px"
          borderColor="teal.100"
        >
          {task}
        </Box>
      )}

      {/* Result preview */}
      {hasResult && resultMessage?.content && (
        <Box
          px="3"
          py="2"
          fontSize="xs"
          fontFamily="mono"
          bg="gray.50"
          _dark={{ bg: 'gray.800' }}
          whiteSpace="pre-wrap"
          overflowX="auto"
          maxH="200px"
          overflowY="auto"
          borderTopWidth="1px"
          borderColor="teal.100"
        >
          {resultMessage.content}
        </Box>
      )}
    </Box>
  )
}
