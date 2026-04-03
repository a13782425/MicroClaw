/**
 * NodeConfigDialog — 点击画布节点后弹出的属性配置面板。
 * 支持编辑 label、type、agentId（Agent）、functionName（Function/Tool）、
 * providerId（SwitchModel）和条件路由标签。
 */
import { useState, useEffect } from 'react'
import {
  Box,
  Button,
  Dialog,
  HStack,
  Input,
  NativeSelect,
  Text,
  Textarea,
  VStack,
} from '@chakra-ui/react'
import { Plus, Trash2, ChevronDown, ChevronRight } from 'lucide-react'
import type { WorkflowNodeConfig, AgentConfig, ProviderConfig } from '@/api/gateway'
import { listAgentTools } from '@/api/gateway'

const NODE_TYPES = ['Agent', 'Function', 'Tool', 'Router', 'SwitchModel', 'Start', 'End'] as const

interface NodeConfigDialogProps {
  node: WorkflowNodeConfig | null
  agents: AgentConfig[]
  providers: ProviderConfig[]
  onClose: () => void
  onSave: (updated: WorkflowNodeConfig) => void
}

export function NodeConfigDialog({ node, agents, providers, onClose, onSave }: NodeConfigDialogProps) {
  const [label, setLabel] = useState('')
  const [type, setType] = useState<WorkflowNodeConfig['type']>('Agent')
  const [agentId, setAgentId] = useState('')
  const [functionName, setFunctionName] = useState('')
  const [providerId, setProviderId] = useState('')
  const [condition, setCondition] = useState('')
  const [configEntries, setConfigEntries] = useState<[string, string][]>([])
  const [showConfig, setShowConfig] = useState(false)
  const [toolAgentId, setToolAgentId] = useState('')
  const [mcpTools, setMcpTools] = useState<{ name: string; group: string }[]>([])

  useEffect(() => {
    if (!node) return
    setLabel(node.label)
    setType(node.type)
    setAgentId(node.agentId ?? '')
    setFunctionName(node.functionName ?? '')
    setProviderId(node.providerId ?? '')
    setToolAgentId(node.config?.toolAgentId ?? '')
    setCondition('')
    setConfigEntries(Object.entries(node.config ?? {}))
    setShowConfig(false)
  }, [node])

  useEffect(() => {
    if (!toolAgentId) { setMcpTools([]); return }
    listAgentTools(toolAgentId)
      .then((res) => {
        const flat = res.groups
          .filter((g) => g.isEnabled)
          .flatMap((g) => g.tools.filter((t) => t.isEnabled).map((t) => ({ name: t.name, group: g.name })))
        setMcpTools(flat)
      })
      .catch(() => setMcpTools([]))
  }, [toolAgentId])

  const handleSave = () => {
    if (!node) return
    const config = configEntries
      .filter(([k]) => k.trim())
      .reduce<Record<string, string>>((acc, [k, v]) => ({ ...acc, [k.trim()]: v }), {})
    if (type === 'Tool' && toolAgentId) {
      config['toolAgentId'] = toolAgentId
    }
    const updated: WorkflowNodeConfig = {
      ...node,
      label: label.trim() || node.label,
      type,
      agentId: type === 'Agent' && agentId ? agentId : null,
      functionName: (type === 'Function' || type === 'Tool') && functionName.trim() ? functionName.trim() : null,
      providerId: type === 'SwitchModel' && providerId ? providerId : null,
      config: Object.keys(config).length > 0 ? config : null,
    }
    onSave(updated)
    onClose()
  }

  return (
    <Dialog.Root
      open={!!node}
      onOpenChange={(e) => { if (!e.open) onClose() }}
      size="sm"
    >
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="420px" bg="white" borderWidth="1px" borderColor="var(--mc-border)">
          <Dialog.Header borderBottomWidth="1px" borderColor="var(--mc-border)" pb="3">
            <Dialog.Title color="var(--mc-text)" fontSize="md">
              节点属性
            </Dialog.Title>
          </Dialog.Header>
          <Dialog.Body pt="4">
            <VStack gap="4" align="stretch">
              {/* 节点类型 */}
              <Box>
                <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">节点类型</Text>
                <NativeSelect.Root size="sm">
                  <NativeSelect.Field
                    value={type}
                    onChange={(e) => setType(e.target.value as WorkflowNodeConfig['type'])}
                    bg="var(--mc-surface-muted)"
                   
                    borderColor="var(--mc-border)"
                    color="var(--mc-text)"
                  >
                    {NODE_TYPES.map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </NativeSelect.Field>
                  <NativeSelect.Indicator />
                </NativeSelect.Root>
              </Box>

              {/* 节点标签 */}
              <Box>
                <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">
                  标签 <Text as="span" color="red.500">*</Text>
                </Text>
                <Input
                  size="sm"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  placeholder="节点显示名称"
                  bg="var(--mc-surface-muted)"
                 
                  borderColor="var(--mc-border)"
                  color="var(--mc-text)"
                />
              </Box>

              {/* Agent 选择（仅 type=Agent 时） */}
              {type === 'Agent' && (
                <Box>
                  <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">绑定 Agent</Text>
                  <NativeSelect.Root size="sm">
                    <NativeSelect.Field
                      value={agentId}
                      onChange={(e) => setAgentId(e.target.value)}
                      bg="var(--mc-surface-muted)"
                     
                      borderColor="var(--mc-border)"
                      color="var(--mc-text)"
                    >
                      <option value="">（未绑定）</option>
                      {agents.map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.name} {a.isDefault ? '（默认）' : ''}
                        </option>
                      ))}
                    </NativeSelect.Field>
                    <NativeSelect.Indicator />
                  </NativeSelect.Root>
                  {agents.length === 0 && (
                    <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">
                      暂无可用 Agent，请先在 Agent 页面创建
                    </Text>
                  )}
                </Box>
              )}

              {/* Function 配置（仅 type=Function 时）— 仅内置函数 */}
              {type === 'Function' && (
                <Box>
                  <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">函数名称</Text>
                  <NativeSelect.Root size="sm">
                    <NativeSelect.Field
                      value={functionName}
                      onChange={(e) => setFunctionName(e.target.value)}
                      bg="var(--mc-surface-muted)"
                     
                      borderColor="var(--mc-border)"
                      color="var(--mc-text)"
                    >
                      <option value="">（选择函数）</option>
                      <option value="uppercase">uppercase — 转大写</option>
                      <option value="lowercase">lowercase — 转小写</option>
                      <option value="trim">trim — 去空格</option>
                    </NativeSelect.Field>
                    <NativeSelect.Indicator />
                  </NativeSelect.Root>
                </Box>
              )}

              {/* Tool 配置（仅 type=Tool 时）— MCP 工具 */}
              {type === 'Tool' && (
                <>
                  <Box>
                    <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">选择 Agent</Text>
                    <NativeSelect.Root size="sm">
                      <NativeSelect.Field
                        value={toolAgentId}
                        onChange={(e) => { setToolAgentId(e.target.value); setFunctionName('') }}
                        bg="var(--mc-surface-muted)"
                       
                        borderColor="var(--mc-border)"
                        color="var(--mc-text)"
                      >
                        <option value="">（选择 Agent）</option>
                        {agents.map((a) => (
                          <option key={a.id} value={a.id}>
                            {a.name} {a.isDefault ? '（默认）' : ''}
                          </option>
                        ))}
                      </NativeSelect.Field>
                      <NativeSelect.Indicator />
                    </NativeSelect.Root>
                  </Box>
                  {toolAgentId && (
                    <Box>
                      <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">选择工具</Text>
                      <NativeSelect.Root size="sm">
                        <NativeSelect.Field
                          value={functionName}
                          onChange={(e) => setFunctionName(e.target.value)}
                          bg="var(--mc-surface-muted)"
                         
                          borderColor="var(--mc-border)"
                          color="var(--mc-text)"
                        >
                          <option value="">（选择工具）</option>
                          {mcpTools.map((t) => (
                            <option key={t.name} value={t.name}>
                              {t.name}（{t.group}）
                            </option>
                          ))}
                        </NativeSelect.Field>
                        <NativeSelect.Indicator />
                      </NativeSelect.Root>
                      {mcpTools.length === 0 && (
                        <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">
                          该 Agent 无可用工具
                        </Text>
                      )}
                    </Box>
                  )}
                </>
              )}

              {/* SwitchModel 配置（仅 type=SwitchModel 时） */}
              {type === 'SwitchModel' && (
                <Box>
                  <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">选择 Provider</Text>
                  <NativeSelect.Root size="sm">
                    <NativeSelect.Field
                      value={providerId}
                      onChange={(e) => setProviderId(e.target.value)}
                      bg="var(--mc-surface-muted)"
                     
                      borderColor="var(--mc-border)"
                      color="var(--mc-text)"
                    >
                      <option value="">（选择 Provider）</option>
                      {providers.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.displayName} {p.isDefault ? '（默认）' : ''}
                        </option>
                      ))}
                    </NativeSelect.Field>
                    <NativeSelect.Indicator />
                  </NativeSelect.Root>
                  {providers.length === 0 && (
                    <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">
                      暂无可用 Provider，请先在模型页面配置
                    </Text>
                  )}
                </Box>
              )}

              {/* 路由条件（type=Router 时提示） */}
              {type === 'Router' && (
                <Box>
                  <Text fontSize="xs" color="var(--mc-text-muted)" mb="1" fontWeight="medium">路由说明</Text>
                  <Textarea
                    size="sm"
                    value={condition}
                    onChange={(e) => setCondition(e.target.value)}
                    placeholder="描述路由判断逻辑（仅供参考，在连线上配置实际条件）"
                    rows={2}
                    bg="var(--mc-surface-muted)"
                   
                    borderColor="var(--mc-border)"
                    color="var(--mc-text)"
                    fontSize="sm"
                  />
                </Box>
              )}

              {/* 节点 ID（只读，供调试） */}
              <Box>
                <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">节点 ID（只读）</Text>
                <Text fontSize="xs" color="var(--mc-text-muted)" fontFamily="mono">
                  {node?.nodeId}
                </Text>
              </Box>

              {/* 扩展配置键值对 */}
              <Box borderTopWidth="1px" borderColor="var(--mc-border)" pt="3">
                <HStack justify="space-between" mb="2">
                  <Text fontSize="xs" color="var(--mc-text-muted)" fontWeight="medium">
                    扩展配置
                    {configEntries.length > 0 && (
                      <Text as="span" color="var(--mc-info)" ml="1">({configEntries.length})</Text>
                    )}
                  </Text>
                  <Button
                    size="2xs"
                    variant="ghost"
                    colorPalette="gray"
                    onClick={() => setShowConfig(!showConfig)}
                  >
                    {showConfig ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
                    {showConfig ? '收起' : '展开'}
                  </Button>
                </HStack>
                {showConfig && (
                  <VStack gap="2" align="stretch">
                    {configEntries.map(([k, v], i) => (
                      <HStack key={i} gap="1.5">
                        <Input
                          size="xs"
                          flex={1}
                          value={k}
                          onChange={(e) => {
                            const updated = [...configEntries]
                            updated[i] = [e.target.value, updated[i][1]]
                            setConfigEntries(updated)
                          }}
                          placeholder="键"
                          fontFamily="mono"
                          bg="var(--mc-surface-muted)"
                         
                          borderColor="var(--mc-border)"
                        />
                        <Input
                          size="xs"
                          flex={2}
                          value={v}
                          onChange={(e) => {
                            const updated = [...configEntries]
                            updated[i] = [updated[i][0], e.target.value]
                            setConfigEntries(updated)
                          }}
                          placeholder="值"
                          bg="var(--mc-surface-muted)"
                         
                          borderColor="var(--mc-border)"
                        />
                        <Button
                          size="xs"
                          variant="ghost"
                          colorPalette="red"
                          onClick={() => setConfigEntries(configEntries.filter((_, j) => j !== i))}
                        >
                          <Trash2 size={11} />
                        </Button>
                      </HStack>
                    ))}
                    <Button
                      size="xs"
                      variant="outline"
                      colorPalette="gray"
                      onClick={() => setConfigEntries([...configEntries, ['', '']])}
                    >
                      <Plus size={11} />
                      添加配置项
                    </Button>
                  </VStack>
                )}
              </Box>
            </VStack>
          </Dialog.Body>
          <Dialog.Footer borderTopWidth="1px" borderColor="var(--mc-border)" pt="3">
            <HStack gap="2" justify="flex-end">
              <Button
                size="sm"
                variant="outline"
                onClick={onClose}
                colorPalette="gray"
              >
                取消
              </Button>
              <Button
                size="sm"
                colorPalette="blue"
                onClick={handleSave}
                disabled={!label.trim()}
              >
                保存
              </Button>
            </HStack>
          </Dialog.Footer>
          <Dialog.CloseTrigger />
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}
