/**
 * WorkflowCanvas — 基于 @xyflow/react 的可视化工作流画布。
 * 支持节点连接、拖拽移动、删除（Delete 键/右键菜单）、边条件点击编辑。
 * 右键画布空白处可添加节点，右键连线可删除连线，顶部工具栏提供保存和运行按钮。
 */
import { useState, useCallback, useEffect, useMemo, type MouseEvent } from 'react'
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  Controls,
  MiniMap,
  Panel,
  Handle,
  Position,
  addEdge,
  useNodesState,
  useEdgesState,
  useReactFlow,
  type Node,
  type Edge,
  type Connection,
  type NodeTypes,
  type NodeChange,
  type EdgeChange,
} from '@xyflow/react'
import { Box, Button, HStack, VStack } from '@chakra-ui/react'
import { Play, Square, Bot, Code, Wrench, GitBranch, RefreshCw, Save, Settings, Trash2 } from 'lucide-react'
import type { WorkflowNodeConfig, WorkflowEdgeConfig, WorkflowConfig } from '@/api/gateway'
import type { NodeExecutionState } from '@/store/workflowStore'

// ────────────────────────────── 常量与自定义节点 ──────────────────────────────

const NODE_COLORS: Record<string, string> = {
  Start: 'var(--mc-node-start)',
  End: 'var(--mc-node-end)',
  Agent: 'var(--mc-node-agent)',
  Function: 'var(--mc-node-function)',
  Tool: 'var(--mc-node-tool)',
  Router: 'var(--mc-node-router)',
  SwitchModel: 'var(--mc-node-switch-model)',
}

const NODE_TYPE_LIST = [
  { type: 'Start' as const, label: '开始', Icon: Play },
  { type: 'End' as const, label: '结束', Icon: Square },
  { type: 'Agent' as const, label: 'Agent', Icon: Bot },
  { type: 'Function' as const, label: '函数', Icon: Code },
  { type: 'Tool' as const, label: '工具', Icon: Wrench },
  { type: 'Router' as const, label: '路由', Icon: GitBranch },
  { type: 'SwitchModel' as const, label: '切换模型', Icon: RefreshCw },
]

const TYPE_LABELS: Record<string, string> = {
  Start: '开始', End: '结束', Agent: 'Agent', Function: '函数',
  Tool: '工具', Router: '路由', SwitchModel: '切换模型',
}

function WorkflowNode({ data }: { data: { label: string; type: string; status?: string } }) {
  const color = NODE_COLORS[data.type] ?? 'var(--mc-text-muted)'
  const entry = NODE_TYPE_LIST.find((n) => n.type === data.type)
  const Icon = entry?.Icon

  const statusColor =
    data.status === 'running' ? 'var(--mc-warning)'
      : data.status === 'completed' ? 'var(--mc-success)'
        : data.status === 'error' ? 'var(--mc-danger)'
          : null

  const leftColor = statusColor ?? color
  const handleStyle = {
    width: 8, height: 8, borderRadius: '50%',
    border: '2px solid var(--mc-bg)',
    background: color,
  }

  return (
    <>
      {data.type !== 'Start' && (
        <Handle type="target" position={Position.Top} style={handleStyle} />
      )}
      <div style={{
        background: 'var(--mc-card)',
        border: '1px solid var(--mc-border)',
        borderLeft: `3px solid ${leftColor}`,
        borderRadius: 8,
        padding: '6px 10px',
        minWidth: 100,
        boxShadow: statusColor
          ? `0 0 8px ${statusColor}40`
          : '0 1px 3px rgba(0,0,0,0.06)',
        transition: 'all 0.2s ease',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          {Icon && <Icon size={13} color={color} style={{ flexShrink: 0 }} />}
          <div>
            <div style={{
              fontSize: 12, fontWeight: 600, lineHeight: 1.3,
              color: 'var(--mc-text)',
            }}>
              {data.label}
            </div>
            <div style={{
              fontSize: 10, color: 'var(--mc-text-muted)', marginTop: 1,
            }}>
              {TYPE_LABELS[data.type] ?? data.type}
            </div>
          </div>
        </div>
      </div>
      {data.type !== 'End' && (
        <Handle type="source" position={Position.Bottom} style={handleStyle} />
      )}
    </>
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
    type: 'smoothstep',
    label: e.label ?? e.condition ?? undefined,
    animated: true,
    style: { stroke: 'var(--mc-border)', strokeWidth: 1.5 },
    labelStyle: { fontSize: 10, fill: 'var(--mc-text-muted)', fontWeight: 500 },
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
      providerId: orig?.providerId,
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
  canvasChanged?: boolean
  canvasSaving?: boolean
  onNodesChange?: (nodes: WorkflowNodeConfig[]) => void
  onEdgesChange?: (edges: WorkflowEdgeConfig[]) => void
  onNodeClick?: (node: WorkflowNodeConfig) => void
  onEdgeClick?: (edge: WorkflowEdgeConfig) => void
  onSave?: () => void
  onRun?: () => void
}

export function WorkflowCanvas(props: WorkflowCanvasProps) {
  return (
    <ReactFlowProvider>
      <WorkflowCanvasInner {...props} />
    </ReactFlowProvider>
  )
}

function WorkflowCanvasInner({
  workflow,
  nodeStates,
  readOnly = false,
  canvasChanged,
  canvasSaving,
  onNodesChange,
  onEdgesChange,
  onNodeClick,
  onEdgeClick,
  onSave,
  onRun,
}: WorkflowCanvasProps) {
  const { screenToFlowPosition } = useReactFlow()
  const bgLineColor = 'var(--mc-border)'
  const minimapBg = 'var(--mc-bg)'

  const [contextMenu, setContextMenu] = useState<{ nodeId: string; x: number; y: number } | null>(null)
  const [edgeMenu, setEdgeMenu] = useState<{ edgeId: string; source: string; target: string; x: number; y: number } | null>(null)
  const [paneMenu, setPaneMenu] = useState<{ x: number; y: number; flowX: number; flowY: number } | null>(null)

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

  useEffect(() => {
    setNodes(toFlowNodes(workflow.nodes, nodeStates))
    setEdges(toFlowEdges(workflow.edges))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflow.id])

  useEffect(() => {
    setNodes((prev) =>
      prev.map((n) => ({
        ...n,
        data: { ...n.data, status: nodeStates[n.id]?.status },
      })),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodeStates])

  useEffect(() => {
    setEdges((prev) =>
      prev.map((flowEdge) => {
        const wfEdge = workflow.edges.find(
          (e) => e.sourceNodeId === flowEdge.source && e.targetNodeId === flowEdge.target,
        )
        return { ...flowEdge, label: wfEdge?.label ?? wfEdge?.condition ?? undefined }
      }),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflow.edges])

  const onConnect = useCallback(
    (params: Connection) => {
      if (readOnly) return
      const newEdges = addEdge(
        { ...params, type: 'smoothstep', animated: true, style: { stroke: 'var(--mc-border)', strokeWidth: 1.5 } },
        edges,
      )
      setEdges(newEdges)
      onEdgesChange?.(fromFlowEdges(newEdges, workflow.edges))
    },
    [readOnly, edges, setEdges, onEdgesChange, workflow.edges],
  )

  const handleNodesChange = useCallback(
    (changes: NodeChange[]) => {
      onNodesStateChange(changes)
      if (!readOnly && onNodesChange) {
        const needsSync = changes.some(
          (c) => c.type === 'remove' || (c.type === 'position' && !c.dragging),
        )
        if (needsSync) {
          setNodes((current) => {
            onNodesChange(fromFlowNodes(current, workflow.nodes))
            return current
          })
        }
      }
    },
    [onNodesStateChange, readOnly, onNodesChange, workflow.nodes, setNodes],
  )

  const handleNodeClick = useCallback(
    (_: MouseEvent, flowNode: Node) => {
      closeAllMenus()
      if (readOnly || !onNodeClick) return
      const original = workflow.nodes.find((n) => n.nodeId === flowNode.id)
      if (original) onNodeClick(original)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [readOnly, onNodeClick, workflow.nodes],
  )

  const handleAddNode = useCallback(
    (type: WorkflowNodeConfig['type'], position?: { x: number; y: number }) => {
      const newNodeId = crypto.randomUUID()
      const pos = position ?? { x: 80 + Math.random() * 300, y: 80 + Math.random() * 200 }
      const newFlowNode: Node = {
        id: newNodeId,
        type: 'workflowNode',
        position: pos,
        data: { label: type, type },
      }
      const newStoreNode: WorkflowNodeConfig = {
        nodeId: newNodeId,
        label: type,
        type,
        agentId: null,
        functionName: null,
        providerId: null,
        position: pos,
      }
      setNodes((prev) => {
        const updated = [...prev, newFlowNode]
        onNodesChange?.(fromFlowNodes(updated, [...workflow.nodes, newStoreNode]))
        return updated
      })
    },
    [setNodes, onNodesChange, workflow.nodes],
  )

  const handleEdgesChange = useCallback(
    (changes: EdgeChange[]) => {
      onEdgesStateChange(changes)
      if (!readOnly && changes.some((c) => c.type === 'remove')) {
        setEdges((current) => {
          onEdgesChange?.(fromFlowEdges(current, workflow.edges))
          return current
        })
      }
    },
    [onEdgesStateChange, readOnly, onEdgesChange, workflow.edges, setEdges],
  )

  const handleEdgeClick = useCallback(
    (_: MouseEvent, flowEdge: Edge) => {
      closeAllMenus()
      if (readOnly || !onEdgeClick) return
      const original = workflow.edges.find(
        (e) => e.sourceNodeId === flowEdge.source && e.targetNodeId === flowEdge.target,
      )
      if (original) onEdgeClick(original)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [readOnly, onEdgeClick, workflow.edges],
  )

  // ── 右键菜单 ─────────────────────────────────────────────────────────

  const closeAllMenus = useCallback(() => {
    setContextMenu(null)
    setEdgeMenu(null)
    setPaneMenu(null)
  }, [])

  const handleNodeContextMenu = useCallback(
    (e: MouseEvent, node: Node) => {
      if (readOnly) return
      e.preventDefault()
      setContextMenu({ nodeId: node.id, x: e.clientX, y: e.clientY })
      setEdgeMenu(null)
      setPaneMenu(null)
    },
    [readOnly],
  )

  const handleEdgeContextMenu = useCallback(
    (e: MouseEvent, edge: Edge) => {
      if (readOnly) return
      e.preventDefault()
      setEdgeMenu({ edgeId: edge.id, source: edge.source, target: edge.target, x: e.clientX, y: e.clientY })
      setContextMenu(null)
      setPaneMenu(null)
    },
    [readOnly],
  )

  const handlePaneContextMenu = useCallback(
    (e: MouseEvent | globalThis.MouseEvent) => {
      if (readOnly) return
      e.preventDefault()
      const flowPos = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      setPaneMenu({ x: e.clientX, y: e.clientY, flowX: flowPos.x, flowY: flowPos.y })
      setContextMenu(null)
      setEdgeMenu(null)
    },
    [readOnly, screenToFlowPosition],
  )

  const handleContextMenuDelete = useCallback(() => {
    if (!contextMenu) return
    const { nodeId } = contextMenu
    setNodes((prev) => {
      const updated = prev.filter((n) => n.id !== nodeId)
      onNodesChange?.(fromFlowNodes(updated, workflow.nodes))
      return updated
    })
    setEdges((prev) => {
      const updated = prev.filter((e) => e.source !== nodeId && e.target !== nodeId)
      onEdgesChange?.(fromFlowEdges(updated, workflow.edges))
      return updated
    })
    setContextMenu(null)
  }, [contextMenu, setNodes, setEdges, onNodesChange, onEdgesChange, workflow.nodes, workflow.edges])

  const handleContextMenuConfigure = useCallback(() => {
    if (!contextMenu || !onNodeClick) return
    const n = workflow.nodes.find((nd) => nd.nodeId === contextMenu.nodeId)
    if (n) onNodeClick(n)
    setContextMenu(null)
  }, [contextMenu, onNodeClick, workflow.nodes])

  const handleEdgeMenuConfigure = useCallback(() => {
    if (!edgeMenu || !onEdgeClick) return
    const original = workflow.edges.find(
      (e) => e.sourceNodeId === edgeMenu.source && e.targetNodeId === edgeMenu.target,
    )
    if (original) onEdgeClick(original)
    setEdgeMenu(null)
  }, [edgeMenu, onEdgeClick, workflow.edges])

  const handleEdgeMenuDelete = useCallback(() => {
    if (!edgeMenu) return
    setEdges((prev) => {
      const updated = prev.filter((e) => e.id !== edgeMenu.edgeId)
      onEdgesChange?.(fromFlowEdges(updated, workflow.edges))
      return updated
    })
    setEdgeMenu(null)
  }, [edgeMenu, setEdges, onEdgesChange, workflow.edges])

  const handlePaneMenuAdd = useCallback(
    (type: WorkflowNodeConfig['type']) => {
      if (!paneMenu) return
      handleAddNode(type, { x: paneMenu.flowX, y: paneMenu.flowY })
      setPaneMenu(null)
    },
    [paneMenu, handleAddNode],
  )

  return (
    <Box h="100%" w="100%" bg="var(--mc-bg)" borderRadius="md" overflow="hidden" position="relative">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={handleNodesChange}
        onEdgesChange={handleEdgesChange}
        onConnect={onConnect}
        onNodeClick={handleNodeClick}
        onEdgeClick={handleEdgeClick}
        onNodeContextMenu={handleNodeContextMenu}
        onEdgeContextMenu={handleEdgeContextMenu}
        onPaneContextMenu={handlePaneContextMenu}
        onPaneClick={closeAllMenus}
        nodesDraggable={!readOnly}
        nodesConnectable={!readOnly}
        elementsSelectable={!readOnly}
        deleteKeyCode="Delete"
        fitView
        fitViewOptions={{ padding: 0.3 }}
        proOptions={{ hideAttribution: true }}
      >
        <Background color={bgLineColor} gap={20} />
        <Controls />
        <MiniMap
          nodeColor={(n) => NODE_COLORS[(n.data as { type: string }).type] ?? 'var(--mc-text-muted)'}
          style={{ background: minimapBg }}
        />
        {!readOnly && (
          <Panel position="top-left">
            <HStack
              gap="1.5"
              bg="var(--mc-card)"
              p="1.5"
              borderRadius="lg"
              shadow="sm"
              borderWidth="1px"
              borderColor="var(--mc-border)"
            >
              <Button
                size="2xs"
                variant={canvasChanged ? 'solid' : 'outline'}
                colorPalette={canvasChanged ? 'blue' : 'whiteAlpha'}
                onClick={onSave}
                loading={canvasSaving}
              >
                <Save size={12} />
                保存
              </Button>
              <Button size="2xs" variant="outline" colorPalette="green" onClick={onRun}>
                <Play size={12} />
                运行
              </Button>
            </HStack>
          </Panel>
        )}
      </ReactFlow>

      {/* 节点右键菜单 */}
      {contextMenu && (
        <Box
          position="fixed"
          top={`${contextMenu.y}px`}
          left={`${contextMenu.x}px`}
          zIndex={9999}
          bg="var(--mc-card)"
          borderRadius="lg"
          shadow="lg"
          borderWidth="1px"
          borderColor="var(--mc-border)"
          overflow="hidden"
          minW="130px"
        >
          <VStack gap="0" align="stretch">
            {onNodeClick && (
              <Button
                size="sm"
                variant="ghost"
                w="full"
                justifyContent="flex-start"
                gap="2"
                borderRadius="0"
                onClick={handleContextMenuConfigure}
              >
                <Settings size={13} />
                配置节点
              </Button>
            )}
            <Button
              size="sm"
              variant="ghost"
              colorPalette="red"
              w="full"
              justifyContent="flex-start"
              gap="2"
              borderRadius="0"
              onClick={handleContextMenuDelete}
            >
              <Trash2 size={13} />
              删除节点
            </Button>
          </VStack>
        </Box>
      )}

      {/* 连线右键菜单 */}
      {edgeMenu && (
        <Box
          position="fixed"
          top={`${edgeMenu.y}px`}
          left={`${edgeMenu.x}px`}
          zIndex={9999}
          bg="var(--mc-card)"
          borderRadius="lg"
          shadow="lg"
          borderWidth="1px"
          borderColor="var(--mc-border)"
          overflow="hidden"
          minW="130px"
        >
          <VStack gap="0" align="stretch">
            {onEdgeClick && (
              <Button
                size="sm"
                variant="ghost"
                w="full"
                justifyContent="flex-start"
                gap="2"
                borderRadius="0"
                onClick={handleEdgeMenuConfigure}
              >
                <Settings size={13} />
                配置连线
              </Button>
            )}
            <Button
              size="sm"
              variant="ghost"
              colorPalette="red"
              w="full"
              justifyContent="flex-start"
              gap="2"
              borderRadius="0"
              onClick={handleEdgeMenuDelete}
            >
              <Trash2 size={13} />
              删除连线
            </Button>
          </VStack>
        </Box>
      )}

      {/* 画布右键菜单 — 添加节点 */}
      {paneMenu && (
        <Box
          position="fixed"
          top={`${paneMenu.y}px`}
          left={`${paneMenu.x}px`}
          zIndex={9999}
          bg="var(--mc-card)"
          borderRadius="lg"
          shadow="lg"
          borderWidth="1px"
          borderColor="var(--mc-border)"
          overflow="hidden"
          minW="140px"
          py="1"
        >
          <Box px="3" py="1" fontSize="xs" color="var(--mc-text-muted)" fontWeight="medium">
            添加节点
          </Box>
          {NODE_TYPE_LIST.map(({ type, label, Icon }) => (
            <Button
              key={type}
              size="sm"
              variant="ghost"
              w="full"
              justifyContent="flex-start"
              gap="2"
              borderRadius="0"
              onClick={() => handlePaneMenuAdd(type)}
              fontWeight="normal"
            >
              <Icon size={13} color={NODE_COLORS[type]} />
              {label}
            </Button>
          ))}
        </Box>
      )}
    </Box>
  )
}
