import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Spinner, Tabs, Button, HStack, Em,
} from '@chakra-ui/react'
import { RefreshCw } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { eventBus } from '@/services/eventBus'
import { listSessions, type SessionInfo } from '@/api/gateway'
import { SessionList } from './session-list'
import { SessionDnaTab } from './session-dna-tab'
import { SessionMemoryTab } from './session-memory-tab'
import { ApprovalTab } from './approval-tab'

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
      setSelected((prev) => prev ? (data.find((session) => session.id === prev.id) ?? prev) : null)
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
    setSessions((prev) => prev.map((session) => (session.id === updated.id ? updated : session)))
    setSelected(updated)
  }

  return (
    <Flex h="100%" overflow="hidden">
      <Flex direction="column" w="300px" minW="300px" borderRightWidth="1px" overflow="hidden">
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
                <SessionDnaTab session={selected} />
              </Tabs.Content>
              <Tabs.Content value="memory" flex="1" overflow="hidden" p="0">
                <SessionMemoryTab session={selected} />
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
