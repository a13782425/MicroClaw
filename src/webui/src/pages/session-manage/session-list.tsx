import { useState } from 'react'
import {
  Box, Flex, Text, Badge, Input, HStack, For,
} from '@chakra-ui/react'
import type { SessionInfo } from '@/api/gateway'

interface SessionListProps {
  sessions: SessionInfo[]
  selected: SessionInfo | null
  onSelect: (session: SessionInfo) => void
}

export function SessionList({ sessions, selected, onSelect }: SessionListProps) {
  const [query, setQuery] = useState('')
  const filtered = sessions.filter((session) =>
    session.title.toLowerCase().includes(query.toLowerCase()),
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
          {(session) => {
            const isActive = selected?.id === session.id
            return (
              <Box
                key={session.id}
                px="3" py="2"
                cursor="pointer"
                borderBottomWidth="1px"
                bg={isActive ? 'blue.50' : undefined}
               
                _hover={{ bg: isActive ? 'blue.50' : 'gray.50', _dark: { bg: isActive ? 'blue.900' : 'gray.800' } }}
                onClick={() => onSelect(session)}
              >
                <Text fontSize="sm" fontWeight="medium" truncate>{session.title}</Text>
                <HStack mt="1" gap="1" flexWrap="wrap">
                  <Badge size="xs" colorPalette="gray" variant="outline">{session.channelType}</Badge>
                  {session.isApproved
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
