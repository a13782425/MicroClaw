/**
 * WorkflowCanvas — 基于 @xyflow/react 的可视化工作流画布。
 * 显示节点和连线，支持基本拖拽编辑后保存。
 */
import { useCallback, useEffect, useMemo, type MouseEvent } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  addEdge,
  useNodesState,
  useEdgesState,
  type Node,
  type Edge,
  type Connection,
  type NodeTypes,
} from '@xyflow/react'
import { Box, Badge, Button, HStack } from '@chakra-ui/react'
import { Plus } from 'lucide-react'
import { useColorModeValue } from '@/components/ui/color-mode'
import type { WorkflowNodeConfig, WorkflowEdgeConfig, WorkflowConfig } from '@/api/gateway'
import type { NodeExecutionState } from '@/store/workflowStore'

// ────────────────────────────── 自定义节点 ──────────────────────────────────

const NODE_COLORS: Record<string, string> = {
  Start: '#22c55e',
  End: '#ef4444',
  Agent: '#3b82f6',
  Function: '#a855f7',
  Router: '#f59e0b',
}

const NODE_TYPE_LIST: { type: WorkflowNodeConfig['type']; colorPalette: string }[] = [
  { type: 'Start', colorPalette: 'green' },
  { type: 'End', colorPalette: 'red' },
  { type: 'Agent', colorPalette: 'blue' },
  { type: 'Function', colorPalette: 'purple' },
  { type: 'Router', colorPalette: 'orange' },
]

function WorkflowNode({ data }: { data: { label: string; type: string; status?: string } }) {
  const bg = NODE_COLORS[data.type] ?? '#64748b'
  const statusColor =
    data.status === 'running'
      ? '#fbbf24'
      : data.status === 'completed'
        ? '#22c55e'
        : data.status === 'error'
          ? '#ef4444'
          : undefined

  return (
    <Box
      bg={bg}
      color="white"
      px="3"
      py="2"
      borderRadius="md"
      fontSize="sm"
      fontWeight="medium"
      minW="100px"
      textAlign="center"
      border={statusColor ? `2px solid ${statusColor}` : '2px solid transparent'}
      boxShadow={statusColor ? `0 0 8px ${statusColor}` : 'md'}
      transition="all 0.2s"
    >
      <Badge size="xs" colorPalette="whiteAlpha" mb="1" display="block">
        {data.type}
      </Badge>
      {data.label}
    </Box>
  )
}

const nodeTypes: NodeTypes = {
  workflowNode: WorkflowNode,
}

// ──────────────────── 转换辅助函数 ──────────────────────────────────────────

function toFlowNodes(
  nodes: WorkflowNodeConfig[],
  nodeStates: Record<string, NodeExecutionState>,
): Node[] {
  return nodes.map((n, i) => ({
    id: n.nodeId,
    type: 'workflowNode',
    position: n.position ?? { x: 200 * (i % 4), y: 150 * Math.floor(i / 4) },
    data: {
      label: n.label,
      type: n.type,
      status: nodeStates[n.nodeId]?.status,
    },
  }))
}

function toFlowEdges(edges: WorkflowEdgeConfig[]): Edge[] {
  return edges.map((e, i) => ({
    id: `${e.sourceNodeId}-${e.targetNodeId}-${i}`,
    source: e.sourceNodeId,
    target: e.targetNodeId,
    label: e.label ?? e.condition ?? undefined,
    animated: true,
    style: { stroke: '#94a3b8' },
    labelStyle: { fontSize: 11, fill: '#94a3b8' },
  }))
}

function fromFlowNodes(flowNodes: Node[], original: WorkflowNodeConfig[]): WorkflowNodeConfig[] {
  return flowNodes.map((fn) => {
    const orig = original.find((n) => n.nodeId === fn.id)
    return {
      nodeId: fn.id,
      label: (fn.data as { label: string }).label,
      type: (fn.data as { type: string }).type as WorkflowNodeConfig['type'],
      agentId: orig?.agentId,
      functionName: orig?.functionName,
      config: orig?.config,
      position: { x: fn.position.x, y: fn.position.y },
    }
  })
}

function fromFlowEdges(flowEdges: Edge[], original: WorkflowEdgeConfig[]): WorkflowEdgeConfig[] {
  return flowEdges.map((fe) => {
    const orig = original.find(
      (e) => e.sourceNodeId === fe.source && e.targetNodeId === fe.target,
    )
    return {
      sourceNodeId: fe.source,
      targetNodeId: fe.target,
      condition: orig?.condition,
      label: typeof fe.label === 'string' ? fe.label : undefined,
    }
  })
}

// ─────────────────────────── 主组件 ─────────────────────────────────────────

interface WorkflowCanvasProps {
  workflow: WorkflowConfig
  nodeStates: Record<string, NodeExecutionState>
  readOnly?: boolean
  onNodesChange?: (nodes: WorkflowNodeConfig[]) => void
  onEdgesChange?: (edges: WorkflowEdgeConfig[]) => void
  onNodeClick?: (node: WorkflowNodeConfig) => void
}

export function WorkflowCanvas({
  workflow,
  nodeStates,
  readOnly = false,
  onNodesChange,
  onEdgesChange,
  onNodeClick,
}: WorkflowCanvasProps) {
  const bgLineColor = useColorModeValue('#e2e8f0', '#334155')
  const minimapBg = useColorModeValue('#f8fafc', '#1e293b')

  const initialNodes = useMemo(
    () => toFlowNodes(workflow.nodes, nodeStates),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [workflow.id],
  )
  const initialEdges = useMemo(
    () => toFlowEdges(workflow.edges),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [workflow.id],
  )

  const [nodes, setNodes, onNodesStateChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesStateChange] = useEdgesState(initialEdges)

  // 当 workflow 切换时重置画布
  useEffect(() => {
    setNodes(toFlowNodes(workflow.nodes, nodeStates))
    setEdges(toFlowEdges(workflow.edges))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflow.id])

  // 当执行状态变化时更新节点颜色（不重置位置）
  useEffect(() => {
    setNodes((prev) =>
      prev.map((n) => ({
        ...n,
        data: {
          ...n.data,
          status: nodeStates[n.id]?.status,
        },
      })),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodeStates])

  const onConnect = useCallback(
    (params: Connection) => {
      if (readOnly) return
      const newEdges = addEdge({ ...params, animated: true }, edges)
      setEdges(newEdges)
      onEdgesChange?.(fromFlowEdges(newEdges, workflow.edges))
    },
    [readOnly, edges, setEdges, onEdgesChange, workflow.edges],
  )

  const handleNodesChange = useCallback(
    (changes: Parameters<typeof onNodesStateChange>[0]) => {
      onNodesStateChange(changes)
      if (!readOnly && onNodesChange) {
        // 在拖拽结束后通知父组件（节流到 dragStop 事件，此处简化为每次变化）
        setNodes((current) => {
          onNodesChange(fromFlowNodes(current, workflow.nodes))
          return current
        })
      }
    },
    [onNodesStateChange, readOnly, onNodesChange, workflow.nodes, setNodes],
  )

  const handleNodeClick = useCallback(
    (_: MouseEvent, flowNode: Node) => {
      if (readOnly || !onNodeClick) return
      const original = workflow.nodes.find((n) => n.nodeId === flowNode.id)
      if (original) onNodeClick(original)
    },
    [readOnly, onNodeClick, workflow.nodes],
  )

  const handleAddNode = useCallback(
    (type: WorkflowNodeConfig['type']) => {
      const newNodeId = crypto.randomUUID()
      const position = { x: 80 + Math.random() * 300, y: 80 + Math.random() * 200 }
      const newFlowNode: Node = {
        id: newNodeId,
        type: 'workflowNode',
        position,
        data: { label: type, type },
      }
      const newStoreNode: WorkflowNodeConfig = {
        nodeId: newNodeId,
        label: type,
        type,
        agentId: null,
        functionName: null,
        position,
      }
      setNodes((prev) => {
        const updated = [...prev, newFlowNode]
        onNodesChange?.(fromFlowNodes(updated, [...workflow.nodes, newStoreNode]))
        return updated
      })
    },
    [setNodes, onNodesChange, workflow.nodes],
  )

  return (
    <Box h="100%" w="100%" bg="gray.100" _dark={{ bg: 'gray.950' }} borderRadius="md" overflow="hidden">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={handleNodesChange}
        onEdgesChange={onEdgesStateChange}
        onConnect={onConnect}
        onNodeClick={handleNodeClick}
        nodesDraggable={!readOnly}
        nodesConnectable={!readOnly}
        elementsSelectable={!readOnly}
        fitView
        fitViewOptions={{ padding: 0.3 }}
      >
        <Background color={bgLineColor} gap={20} />
        <Controls />
        <MiniMap
          nodeColor={(n) => NODE_COLORS[(n.data as { type: string }).type] ?? '#64748b'}
          style={{ background: minimapBg }}
        />
        {!readOnly && (
          <Panel position="top-left">
            <HStack
              gap="1"
              bg="white"
              _dark={{ bg: 'gray.800', borderColor: 'gray.700' }}
              p="1.5"
              borderRadius="md"
              shadow="sm"
              borderWidth="1px"
              borderColor="gray.200"
            >
              <Box fontSize="xs" color="gray.500" px="1" flexShrink={0}>添加节点</Box>
              {NODE_TYPE_LIST.map(({ type, colorPalette }) => (
                <Button
                  key={type}
                  size="2xs"
                  colorPalette={colorPalette}
                  variant="subtle"
                  onClick={() => handleAddNode(type)}
                >
                  <Plus size={10} />
                  {type}
                </Button>
              ))}
            </HStack>
          </Panel>
        )}
      </ReactFlow>
    </Box>
  )
}
