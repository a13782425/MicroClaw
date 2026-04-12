import { useState } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack,
  Input, Switch, Tabs, Select, Portal,
} from '@chakra-ui/react'
import { Trash2 } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  updateAgent,
  deleteAgent,
  type AgentConfig,
} from '@/api/gateway'
import { routingStrategyCollection, ROUTING_STRATEGY_OPTIONS } from './agent-constants'
import { ToolsTab, McpTab, SkillsTab, DnaTab, SubAgentsTab } from './agent-access-tabs'

interface AgentDetailProps {
  agent: AgentConfig
  allAgents: AgentConfig[]
  onUpdated: (agent: AgentConfig) => void
  onDeleted: (id: string) => void
}

export function AgentDetail({ agent, allAgents, onUpdated, onDeleted }: AgentDetailProps) {
  const [deleting, setDeleting] = useState(false)
  const [togglingEnabled, setTogglingEnabled] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [savingRouting, setSavingRouting] = useState(false)
  const [budgetInput, setBudgetInput] = useState(agent.monthlyBudgetUsd != null ? String(agent.monthlyBudgetUsd) : '')
  const [savingBudget, setSavingBudget] = useState(false)
  const [contextWindowInput, setContextWindowInput] = useState(agent.contextWindowMessages != null ? String(agent.contextWindowMessages) : '')
  const [savingContextWindow, setSavingContextWindow] = useState(false)

  const toggleEnabled = async (value: boolean) => {
    setTogglingEnabled(true)
    try {
      await updateAgent({ id: agent.id, isEnabled: value })
      onUpdated({ ...agent, isEnabled: value })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setTogglingEnabled(false)
    }
  }

  const handleRoutingStrategyChange = async (strategy: string) => {
    setSavingRouting(true)
    try {
      await updateAgent({ id: agent.id, routingStrategy: strategy })
      onUpdated({ ...agent, routingStrategy: strategy })
      toaster.create({ type: 'success', title: '路由策略已更新' })
    } catch {
      toaster.create({ type: 'error', title: '路由策略更新失败' })
    } finally {
      setSavingRouting(false)
    }
  }

  const handleBudgetSave = async () => {
    const parsed = budgetInput.trim() === '' ? null : parseFloat(budgetInput)
    if (budgetInput.trim() !== '' && (isNaN(parsed!) || parsed! < 0)) {
      toaster.create({ type: 'error', title: '预算须为非负数字或留空（不限制）' })
      return
    }
    setSavingBudget(true)
    try {
      await updateAgent({ id: agent.id, monthlyBudgetUsd: parsed, hasMonthlyBudgetUsd: true })
      onUpdated({ ...agent, monthlyBudgetUsd: parsed })
      toaster.create({ type: 'success', title: parsed == null ? '月度预算已清除' : '月度预算已更新' })
    } catch {
      toaster.create({ type: 'error', title: '月度预算更新失败' })
    } finally {
      setSavingBudget(false)
    }
  }

  const handleContextWindowSave = async () => {
    const parsed = contextWindowInput.trim() === '' ? null : parseInt(contextWindowInput, 10)
    if (contextWindowInput.trim() !== '' && (isNaN(parsed!) || parsed! < 1)) {
      toaster.create({ type: 'error', title: '上下文窗口须为正整数或留空（不限制）' })
      return
    }
    setSavingContextWindow(true)
    try {
      await updateAgent({ id: agent.id, contextWindowMessages: parsed, hasContextWindowMessages: true })
      onUpdated({ ...agent, contextWindowMessages: parsed })
      toaster.create({ type: 'success', title: parsed == null ? '上下文窗口限制已清除' : '上下文窗口已更新' })
    } catch {
      toaster.create({ type: 'error', title: '上下文窗口更新失败' })
    } finally {
      setSavingContextWindow(false)
    }
  }

  const handleDelete = async () => {
    setDeleting(true)
    try {
      await deleteAgent(agent.id)
      toaster.create({ type: 'success', title: 'Agent 已删除' })
      onDeleted(agent.id)
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleting(false)
      setConfirmOpen(false)
    }
  }

  return (
    <Flex direction="column" flex="1" overflow="hidden">
      <HStack px="4" py="3" borderBottomWidth="1px" gap="3" flexWrap="wrap">
        <Box w="10" h="10" rounded="full" bg="var(--mc-info)" display="flex" alignItems="center" justifyContent="center">
          <Text color="white" fontWeight="bold" fontSize="lg">{agent.name[0]?.toUpperCase()}</Text>
        </Box>
        <Box flex="1" minW="0">
          <HStack gap="2">
            <Text fontWeight="semibold" truncate>{agent.name}</Text>
            {agent.isDefault && <Badge colorPalette="yellow" size="sm">DEFAULT</Badge>}
          </HStack>
        </Box>
        <HStack>
          <Switch.Root size="sm" checked={agent.isEnabled} disabled={togglingEnabled} onCheckedChange={(e) => toggleEnabled(e.checked)}>
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="sm">{agent.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>
          <Button
            size="sm"
            variant="outline"
            colorPalette="red"
            loading={deleting}
            disabled={agent.isDefault}
            title={agent.isDefault ? '默认 Agent 不可删除' : '删除 Agent'}
            onClick={() => setConfirmOpen(true)}
          >
            <Trash2 size={14} />
          </Button>
        </HStack>
      </HStack>

      <ConfirmDialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        onConfirm={handleDelete}
        title="删除 Agent"
        description={`确认删除 Agent「${agent.name}」？`}
        confirmText="删除"
        loading={deleting}
      />

      <Tabs.Root defaultValue="overview" flex="1" overflow="hidden" display="flex" flexDirection="column">
        <Tabs.List px="3" bg="var(--mc-input)" borderBottomWidth="1px" borderColor="var(--mc-border)">
          <Tabs.Trigger
            value="overview"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            概览
          </Tabs.Trigger>
          <Tabs.Trigger
            value="dna"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            🧬 DNA
          </Tabs.Trigger>
          <Tabs.Trigger
            value="sub-agents"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            子代理
          </Tabs.Trigger>
          <Tabs.Trigger
            value="tools"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            工具
          </Tabs.Trigger>
          <Tabs.Trigger
            value="mcp"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            MCP
          </Tabs.Trigger>
          <Tabs.Trigger
            value="skills"
            color="var(--mc-text-muted)"
            _hover={{ bg: 'var(--mc-card-hover)', color: 'var(--mc-text)' }}
            _selected={{ bg: 'var(--mc-selected-bg)', color: 'var(--mc-text)', fontWeight: 'semibold' }}
          >
            技能
          </Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="overview" flex="1" overflowY="auto">
          <Box p="4">
            <HStack gap="4" mb="4" flexWrap="wrap">
              <VStack gap="0" align="start" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="var(--mc-text-muted)">禁用 MCP</Text>
                <Text fontWeight="semibold">{agent.disabledMcpServerIds.length} 个</Text>
              </VStack>
              <VStack gap="0" align="start" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="var(--mc-text-muted)">禁用技能</Text>
                <Text fontWeight="semibold">{agent.disabledSkillIds.length} 个</Text>
              </VStack>
              <VStack gap="0" align="start" bg="var(--mc-surface-muted)" p="3" rounded="md" minW="100px">
                <Text fontSize="xs" color="var(--mc-text-muted)">状态</Text>
                <Badge colorPalette={agent.isEnabled ? 'green' : 'gray'} size="sm">
                  {agent.isEnabled ? '启用' : '停用'}
                </Badge>
              </VStack>
            </HStack>
            <Box>
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">描述</Text>
              {agent.description
                ? <Text fontSize="sm" whiteSpace="pre-wrap">{agent.description}</Text>
                : <Text fontSize="sm" color="var(--mc-text-muted)">（未设置）</Text>
              }
            </Box>

            <Box mt="4">
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">Provider 路由策略</Text>
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">当会话未绑定具体 Provider 时，按此策略从已启用的 Provider 中自动选择。</Text>
              <Select.Root
                value={[agent.routingStrategy ?? 'Default']}
                onValueChange={(value) => handleRoutingStrategyChange(value.value[0])}
                collection={routingStrategyCollection}
                size="sm"
                disabled={savingRouting}
              >
                <Select.Trigger maxW="260px" bg="var(--mc-surface-muted)" borderColor="var(--mc-border)" color="var(--mc-text)">
                  <Select.ValueText placeholder="选择路由策略" />
                </Select.Trigger>
                <Portal>
                  <Select.Positioner>
                    <Select.Content>
                      {ROUTING_STRATEGY_OPTIONS.map((option) => (
                        <Select.Item key={option.value} item={option}>{option.label}</Select.Item>
                      ))}
                    </Select.Content>
                  </Select.Positioner>
                </Portal>
              </Select.Root>
              {savingRouting && <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">保存中…</Text>}
            </Box>

            <Box mt="4">
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">月度预算上限（USD）</Text>
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">超过 80% 记录预警日志，超过 100% 记录超限日志。留空表示不限制。</Text>
              <HStack>
                <Input size="sm" maxW="160px" type="number" min="0" step="0.01" placeholder="不限制" value={budgetInput} onChange={(e) => setBudgetInput(e.target.value)} />
                <Button size="sm" colorPalette="blue" loading={savingBudget} onClick={handleBudgetSave}>保存</Button>
              </HStack>
            </Box>

            <Box mt="4">
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">上下文窗口（条）</Text>
              <Text fontSize="xs" color="var(--mc-text-muted)" mb="2">发送给 LLM 的最近消息条数。配合 RAG 使用时建议设为 20-50，留空表示全量历史（不限制）。</Text>
              <HStack>
                <Input size="sm" maxW="160px" type="number" min="1" step="1" placeholder="不限制" value={contextWindowInput} onChange={(e) => setContextWindowInput(e.target.value)} />
                <Button size="sm" colorPalette="blue" loading={savingContextWindow} onClick={handleContextWindowSave}>保存</Button>
              </HStack>
            </Box>

            {agent.exposeAsA2A && (
              <Box mt="4">
                <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">A2A 端点</Text>
                <Box
                  bg="var(--mc-surface-muted)"
                 
                  p="2"
                  rounded="md"
                  fontFamily="mono"
                  fontSize="xs"
                  wordBreak="break-all"
                  cursor="text"
                  userSelect="all"
                >
                  {`${window.location.origin}/a2a/agent/${agent.id}`}
                </Box>
              </Box>
            )}
          </Box>
        </Tabs.Content>

        <Tabs.Content value="tools" flex="1" overflowY="auto" p="0">
          <ToolsTab agent={agent} />
        </Tabs.Content>

        <Tabs.Content value="sub-agents" flex="1" overflowY="auto" p="0">
          <SubAgentsTab agent={agent} allAgents={allAgents} onUpdated={onUpdated} />
        </Tabs.Content>

        <Tabs.Content value="dna" flex="1" overflowY="auto" p="0">
          <DnaTab agent={agent} />
        </Tabs.Content>

        <Tabs.Content value="mcp" flex="1" overflowY="auto" p="0">
          <McpTab agent={agent} onUpdated={onUpdated} />
        </Tabs.Content>

        <Tabs.Content value="skills" flex="1" overflowY="auto" p="0">
          <SkillsTab agent={agent} onUpdated={onUpdated} />
        </Tabs.Content>
      </Tabs.Root>
    </Flex>
  )
}
