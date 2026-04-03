import { useState, useEffect, useCallback, useRef } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, Spinner,
} from '@chakra-ui/react'
import { Plus, Bot } from 'lucide-react'
import { listAgents, type AgentConfig } from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { CreateDialog } from './create-dialog'
import { AgentDetail } from './agent-detail'

export default function AgentsPage() {
  const [agents, setAgents] = useState<AgentConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<AgentConfig | null>(null)
  const [showCreate, setShowCreate] = useState(false)
  const loadedRef = useRef(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listAgents()
      setAgents(data)
      setSelected((prev) => prev ? (data.find((agent) => agent.id === prev.id) ?? null) : null)
    } catch {
      toaster.create({ type: 'error', title: '加载 Agent 列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (!loadedRef.current) {
      loadedRef.current = true
      load()
    }
  }, [load])

  const handleUpdated = (updated: AgentConfig) => {
    setAgents((prev) => prev.map((agent) => (agent.id === updated.id ? updated : agent)))
    setSelected(updated)
  }

  const handleDeleted = (id: string) => {
    setAgents((prev) => prev.filter((agent) => agent.id !== id))
    setSelected(null)
  }

  return (
    <Flex h="100%" overflow="hidden">
      <Flex direction="column" w="260px" minW="260px" borderRightWidth="1px" overflow="hidden">
        <HStack px="3" py="2" borderBottomWidth="1px" justify="space-between">
          <Text fontWeight="semibold" fontSize="sm">代理</Text>
          <Button size="xs" colorPalette="blue" onClick={() => setShowCreate(true)}>
            <Plus size={14} /> 新建
          </Button>
        </HStack>

        {loading && agents.length === 0 ? (
          <Box p="6" textAlign="center"><Spinner /></Box>
        ) : (
          <Box flex="1" overflowY="auto" p="2">
            {agents.length === 0 ? (
              <Box py="10" textAlign="center" color="var(--mc-text-muted)">暂无 Agent</Box>
            ) : (
              agents.map((agent) => {
                const isActive = selected?.id === agent.id
                return (
                  <Box
                    key={agent.id}
                    px="3"
                    py="2.5"
                    mb="2"
                    borderWidth="1px"
                    rounded="md"
                    cursor="pointer"
                    bg={isActive ? 'var(--mc-selected-bg)' : 'transparent'}
                    borderColor={isActive ? 'var(--mc-primary)' : 'var(--mc-border)'}
                    _hover={{ bg: isActive ? 'var(--mc-selected-hover-bg)' : 'var(--mc-card-hover)' }}
                    onClick={() => setSelected(agent)}
                  >
                    <HStack gap="2" align="start">
                      <Bot size={16} style={{ marginTop: 2 }} />
                      <Box flex="1" minW="0">
                        <HStack gap="1" mb="1" flexWrap="wrap">
                          <Text fontSize="sm" fontWeight="medium" truncate>{agent.name}</Text>
                          {agent.isDefault && <Badge size="xs" colorPalette="yellow">DEFAULT</Badge>}
                        </HStack>
                        {agent.description && (
                          <Text fontSize="xs" color="var(--mc-text-muted)" lineClamp={2}>{agent.description}</Text>
                        )}
                        <HStack mt="2" gap="1" flexWrap="wrap">
                          <Badge size="xs" colorPalette={agent.isEnabled ? 'green' : 'gray'}>
                            {agent.isEnabled ? '启用' : '停用'}
                          </Badge>
                          {agent.exposeAsA2A && <Badge size="xs" colorPalette="blue" variant="outline">A2A</Badge>}
                        </HStack>
                      </Box>
                    </HStack>
                  </Box>
                )
              })
            )}
          </Box>
        )}
      </Flex>

      {selected ? (
        <AgentDetail key={selected.id} agent={selected} allAgents={agents} onUpdated={handleUpdated} onDeleted={handleDeleted} />
      ) : (
        <Flex flex="1" align="center" justify="center">
          <Text color="var(--mc-text-muted)">从左侧选择一个 Agent</Text>
        </Flex>
      )}

      <CreateDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        onCreated={() => { setShowCreate(false); void load() }}
      />
    </Flex>
  )
}
