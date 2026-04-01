import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Switch, Tabs,
} from '@chakra-ui/react'
import { Plus, Trash2, GitBranch, Play } from 'lucide-react'
import { Dialog } from '@chakra-ui/react'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { useWorkflowStore } from '@/store/workflowStore'
import { WorkflowCanvas } from './workflow-canvas'
import { WorkflowExecutePanel } from './workflow-execute-panel'
import { NodeConfigDialog } from './node-config-dialog'
import { EdgeConfigDialog } from './edge-config-dialog'
import { listAgents, listProviders } from '@/api/gateway'
import { NativeSelect } from '@chakra-ui/react'
import type { WorkflowConfig, WorkflowCreateRequest, WorkflowNodeConfig, WorkflowEdgeConfig, AgentConfig, ProviderConfig } from '@/api/gateway'

// ──────────────────── 创建弹窗 ──────────────────────────────────────────────

function CreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (wf: WorkflowConfig) => void
}) {
  const createWorkflow = useWorkflowStore((s) => s.createWorkflow)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)

  const reset = () => { setName(''); setDescription('') }

  const submit = async () => {
    if (!name.trim()) return
    setSaving(true)
    try {
      const req: WorkflowCreateRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        isEnabled: true,
        nodes: [],
        edges: [],
      }
      const wf = await createWorkflow(req)
      toaster.create({ type: 'success', title: '工作流创建成功' })
      reset()
      onCreated(wf)
      onClose()
    } catch {
      toaster.create({ type: 'error', title: '创建失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) { reset(); onClose() } }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="480px">
          <Dialog.Header>
            <Dialog.Title>新建工作流</Dialog.Title>
          </Dialog.Header>
          <Dialog.Body>
            <VStack gap="3" align="stretch">
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">
                  名称 <Text as="span" color="red.500">*</Text>
                </Text>
                <Input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="工作流名称"
                  onKeyDown={(e) => e.key === 'Enter' && submit()}
                />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">描述</Text>
                <Textarea
                  rows={3}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="功能描述（可选）"
                />
              </Box>
            </VStack>
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>取消</Button>
            <Button colorPalette="blue" loading={saving} onClick={submit} disabled={!name.trim()}>
              创建
            </Button>
          </Dialog.Footer>
          <Dialog.CloseTrigger />
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ──────────────────── 工作流列表项 ─────────────────────────────────────────

function WorkflowItem({
  workflow,
  selected,
  onClick,
  onDelete,
}: {
  workflow: WorkflowConfig
  selected: boolean
  onClick: () => void
  onDelete: () => void
}) {
  return (
    <Box
      px="3"
      py="2.5"
      borderRadius="md"
      cursor="pointer"
      bg={selected ? 'blue.50' : 'transparent'}
      _dark={{ bg: selected ? 'blue.900' : 'transparent', borderColor: selected ? 'blue.600' : 'transparent' }}
      _hover={{ bg: selected ? 'blue.100' : 'gray.100', _dark: { bg: selected ? 'blue.900' : 'gray.800' } }}
      onClick={onClick}
      borderWidth="1px"
      borderColor={selected ? 'blue.400' : 'transparent'}
      transition="all 0.15s"
    >
      <HStack justify="space-between">
        <HStack gap="2" flex={1} minW={0}>
          <GitBranch size={14} color={workflow.isEnabled ? '#60a5fa' : '#64748b'} />
          <Text fontSize="sm" fontWeight="medium" color="gray.900" _dark={{ color: 'gray.100' }} truncate>
            {workflow.name}
          </Text>
        </HStack>
        <HStack gap="1.5" flexShrink={0}>
          <Badge
            size="xs"
            colorPalette={workflow.isEnabled ? 'green' : 'gray'}
            variant="subtle"
          >
            {workflow.isEnabled ? '启用' : '禁用'}
          </Badge>
          <Button
            size="xs"
            variant="ghost"
            colorPalette="red"
            onClick={(e) => { e.stopPropagation(); onDelete() }}
            aria-label="删除工作流"
          >
            <Trash2 size={12} />
          </Button>
        </HStack>
      </HStack>
      {workflow.description && (
        <Text fontSize="xs" color="gray.500" _dark={{ color: 'gray.400' }} mt="0.5" truncate>
          {workflow.description}
        </Text>
      )}
      <Text fontSize="xs" color="gray.400" _dark={{ color: 'gray.600' }} mt="0.5">
        {workflow.nodes.length} 节点 · {workflow.edges.length} 连线
      </Text>
    </Box>
  )
}

// ──────────────────── 工作流详情/编辑区 ────────────────────────────────────

function WorkflowDetail({ workflow }: { workflow: WorkflowConfig }) {
  const { nodeStates, updateWorkflow, updateNodes, updateEdges, updateNode } = useWorkflowStore()
  const [name, setName] = useState(workflow.name)
  const [description, setDescription] = useState(workflow.description)
  const [isEnabled, setIsEnabled] = useState(workflow.isEnabled)
  const [defaultProviderId, setDefaultProviderId] = useState(workflow.defaultProviderId ?? '')
  const [saving, setSaving] = useState(false)
  const [dirty, setDirty] = useState(false)

  const [selectedNode, setSelectedNode] = useState<WorkflowNodeConfig | null>(null)
  const [selectedEdge, setSelectedEdge] = useState<WorkflowEdgeConfig | null>(null)
  const [agents, setAgents] = useState<AgentConfig[]>([])
  const [providers, setProviders] = useState<ProviderConfig[]>([])
  const [canvasChanged, setCanvasChanged] = useState(false)
  const [canvasSaving, setCanvasSaving] = useState(false)
  const [activeTab, setActiveTab] = useState('canvas')

  useEffect(() => {
    setName(workflow.name)
    setDescription(workflow.description)
    setIsEnabled(workflow.isEnabled)
    setDefaultProviderId(workflow.defaultProviderId ?? '')
    setDirty(false)
    setSelectedNode(null)
    setCanvasChanged(false)
    setActiveTab('canvas')
  }, [workflow.id])

  useEffect(() => {
    listAgents().then(setAgents).catch(() => setAgents([]))
    listProviders().then(setProviders).catch(() => setProviders([]))
  }, [])

  const saveBasicInfo = async () => {
    setSaving(true)
    try {
      await updateWorkflow(workflow.id, { name, description, isEnabled, defaultProviderId: defaultProviderId || null })
      toaster.create({ type: 'success', title: '已保存' })
      setDirty(false)
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  const handleNodesChange = useCallback((nodes: WorkflowNodeConfig[]) => {
    updateNodes(nodes)
    setCanvasChanged(true)
  }, [updateNodes])

  const handleEdgesChange = useCallback((edges: WorkflowEdgeConfig[]) => {
    updateEdges(edges)
    setCanvasChanged(true)
  }, [updateEdges])

  const handleNodeClick = useCallback((node: WorkflowNodeConfig) => {
    setSelectedNode(node)
  }, [])

  const handleNodeSave = useCallback((updated: WorkflowNodeConfig) => {
    updateNode(updated)
    setCanvasChanged(true)
    toaster.create({ type: 'success', title: '节点已更新' })
  }, [updateNode])

  const handleEdgeSave = useCallback((updated: WorkflowEdgeConfig) => {
    const newEdges = (workflow.edges ?? []).map((e) =>
      e.sourceNodeId === updated.sourceNodeId && e.targetNodeId === updated.targetNodeId
        ? updated
        : e,
    )
    updateEdges(newEdges)
    setCanvasChanged(true)
    setSelectedEdge(null)
    toaster.create({ type: 'success', title: '连线已更新' })
  }, [workflow.edges, updateEdges])

  const saveCanvas = useCallback(async () => {
    setCanvasSaving(true)
    try {
      await updateWorkflow(workflow.id, {
        nodes: workflow.nodes,
        edges: workflow.edges,
      })
      setCanvasChanged(false)
      toaster.create({ type: 'success', title: '画布已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setCanvasSaving(false)
    }
  }, [workflow.id, workflow.nodes, workflow.edges, updateWorkflow])

  return (
    <Flex direction="column" h="100%" gap="3">
      {/* 顶部基本信息 */}
      <Box bg="white" _dark={{ bg: 'gray.900', borderColor: 'gray.700' }} borderRadius="md" p="3" borderWidth="1px" borderColor="gray.200">
        <Flex gap="3" align="end" wrap="wrap">
          <Box flex={1} minW="160px">
            <Text fontSize="xs" color="gray.500" _dark={{ color: 'gray.400' }} mb="1">名称</Text>
            <Input
              size="sm"
              value={name}
              onChange={(e) => { setName(e.target.value); setDirty(true) }}
              bg="gray.50"
              _dark={{ bg: 'gray.800', borderColor: 'gray.600', color: 'gray.100' }}
              borderColor="gray.300"
              color="gray.900"
            />
          </Box>
          <Box flex={2} minW="200px">
            <Text fontSize="xs" color="gray.500" _dark={{ color: 'gray.400' }} mb="1">描述</Text>
            <Input
              size="sm"
              value={description}
              onChange={(e) => { setDescription(e.target.value); setDirty(true) }}
              bg="gray.50"
              _dark={{ bg: 'gray.800', borderColor: 'gray.600', color: 'gray.100' }}
              borderColor="gray.300"
              color="gray.900"
            />
          </Box>
          <Box minW="160px">
            <Text fontSize="xs" color="gray.500" _dark={{ color: 'gray.400' }} mb="1">默认模型</Text>
            <NativeSelect.Root size="sm">
              <NativeSelect.Field
                value={defaultProviderId}
                onChange={(e) => { setDefaultProviderId(e.target.value); setDirty(true) }}
                bg="gray.50"
                _dark={{ bg: 'gray.800', borderColor: 'gray.600', color: 'gray.100' }}
                borderColor="gray.300"
                color="gray.900"
              >
                <option value="">（跟随全局默认）</option>
                {providers.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.displayName} {p.isDefault ? '（默认）' : ''}
                  </option>
                ))}
              </NativeSelect.Field>
              <NativeSelect.Indicator />
            </NativeSelect.Root>
          </Box>
          <HStack gap="2" alignSelf="center">
            <Text fontSize="sm" color="gray.700" _dark={{ color: 'gray.300' }}>启用</Text>
            <Switch.Root
              checked={isEnabled}
              onCheckedChange={(e) => { setIsEnabled(e.checked); setDirty(true) }}
              size="sm"
            >
              <Switch.HiddenInput />
              <Switch.Control />
            </Switch.Root>
          </HStack>
          {dirty && (
            <Button
              size="sm"
              variant="solid"
              colorPalette="blue"
              loading={saving}
              onClick={saveBasicInfo}
            >
              保存
            </Button>
          )}
        </Flex>
      </Box>

      {/* Tabs: 画布 / 执行 */}
      <Tabs.Root value={activeTab} onValueChange={(e) => setActiveTab(e.value)} flex={1} display="flex" flexDirection="column" overflow="hidden">
        <Tabs.List>
          <Tabs.Trigger value="canvas">
            <GitBranch size={14} />
            画布
          </Tabs.Trigger>
          <Tabs.Trigger value="execute">
            <Play size={14} />
            执行
          </Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="canvas" flex={1} overflow="hidden" p="0" mt="3">
          <Box h="100%" minH="400px">
            <WorkflowCanvas
              workflow={workflow}
              nodeStates={nodeStates}
              canvasChanged={canvasChanged}
              canvasSaving={canvasSaving}
              onNodesChange={handleNodesChange}
              onEdgesChange={handleEdgesChange}
              onNodeClick={handleNodeClick}
              onEdgeClick={(edge) => setSelectedEdge(edge)}
              onSave={saveCanvas}
              onRun={() => setActiveTab('execute')}
            />
          </Box>
        </Tabs.Content>

        <Tabs.Content value="execute" p="3" overflowY="auto">
          <WorkflowExecutePanel workflow={workflow} />
        </Tabs.Content>
      </Tabs.Root>

      {/* 节点属性配置面板 */}
      <NodeConfigDialog
        node={selectedNode}
        agents={agents}
        providers={providers}
        onClose={() => setSelectedNode(null)}
        onSave={handleNodeSave}
      />
      {/* 连线条件配置面板 */}
      <EdgeConfigDialog
        edge={selectedEdge}
        onClose={() => setSelectedEdge(null)}
        onSave={handleEdgeSave}
      />
    </Flex>
  )
}

// ──────────────────── 主页面 ────────────────────────────────────────────────

export default function WorkflowsPage() {
  const { workflows, loading, fetchWorkflows, deleteWorkflow, setCurrentWorkflow, currentWorkflow } =
    useWorkflowStore()

  const [createOpen, setCreateOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<WorkflowConfig | null>(null)

  useEffect(() => {
    fetchWorkflows()
  }, [fetchWorkflows])

  const handleDelete = async () => {
    if (!deleteTarget) return
    try {
      await deleteWorkflow(deleteTarget.id)
      toaster.create({ type: 'success', title: '已删除' })
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteTarget(null)
    }
  }

  return (
    <Flex h="100%" gap="0" overflow="hidden">
      {/* 左侧列表 */}
      <Box
        w="260px"
        flexShrink={0}
        borderRightWidth="1px"
        borderColor="gray.200"
        _dark={{ borderColor: 'gray.700', bg: 'gray.950' }}
        display="flex"
        flexDirection="column"
        bg="gray.50"
      >
        <HStack px="3" py="3" borderBottomWidth="1px" borderColor="gray.200" _dark={{ borderColor: 'gray.700' }} justify="space-between">
          <Text fontWeight="semibold" fontSize="sm" color="gray.800" _dark={{ color: 'gray.200' }}>工作流</Text>
          <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => setCreateOpen(true)}>
            <Plus size={14} />
            新建
          </Button>
        </HStack>

        <Box flex={1} overflowY="auto" p="2">
          {loading && (
            <Flex justify="center" py="8">
              <Spinner size="sm" color="blue.400" />
            </Flex>
          )}
          {!loading && workflows.length === 0 && (
            <VStack py="8" gap="2" color="gray.500">
              <GitBranch size={24} />
              <Text fontSize="sm">暂无工作流</Text>
            </VStack>
          )}
          <VStack gap="1" align="stretch">
            {workflows.map((wf) => (
              <WorkflowItem
                key={wf.id}
                workflow={wf}
                selected={currentWorkflow?.id === wf.id}
                onClick={() => setCurrentWorkflow(wf)}
                onDelete={() => setDeleteTarget(wf)}
              />
            ))}
          </VStack>
        </Box>
      </Box>

      {/* 右侧详情 */}
      <Box flex={1} overflow="hidden" p="3" bg="gray.50" _dark={{ bg: 'gray.950' }}>
        {currentWorkflow ? (
          <WorkflowDetail workflow={currentWorkflow} />
        ) : (
          <Flex h="100%" align="center" justify="center" color="gray.500" direction="column" gap="3">
            <GitBranch size={40} />
            <Text>选择一个工作流开始编辑</Text>
            <Button
              size="sm"
              colorPalette="blue"
              variant="outline"
              onClick={() => setCreateOpen(true)}
            >
              <Plus size={14} />
              新建工作流
            </Button>
          </Flex>
        )}
      </Box>

      {/* 弹窗 */}
      <CreateDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(wf) => setCurrentWorkflow(wf)}
      />
      <ConfirmDialog
        open={!!deleteTarget}
        title="删除工作流"
        description={`确定要删除工作流「${deleteTarget?.name}」吗？此操作不可撤销。`}
        onConfirm={handleDelete}
        onClose={() => setDeleteTarget(null)}
      />
    </Flex>
  )
}
