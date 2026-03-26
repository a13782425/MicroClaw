import { create } from 'zustand'
import {
  type WorkflowConfig,
  type WorkflowCreateRequest,
  type WorkflowUpdateRequest,
  type WorkflowNodeConfig,
  type WorkflowEdgeConfig,
  type SseChunk,
  listWorkflows,
  getWorkflow,
  createWorkflow,
  updateWorkflow,
  deleteWorkflow,
  streamWorkflow,
} from '@/api/gateway'

/** 节点执行状态（在工作流运行时实时更新）。 */
export type NodeExecutionStatus = 'idle' | 'running' | 'completed' | 'error'

export interface NodeExecutionState {
  status: NodeExecutionStatus
  result?: string
  durationMs?: number
  error?: string
}

interface WorkflowState {
  // ── 列表 ──────────────────────────────────────────────────────────────
  workflows: WorkflowConfig[]
  loading: boolean
  error: string | null

  // ── 当前编辑的工作流 ──────────────────────────────────────────────────
  currentWorkflow: WorkflowConfig | null

  // ── 执行状态 ─────────────────────────────────────────────────────────
  executionId: string | null
  executionRunning: boolean
  executionOutput: string
  nodeStates: Record<string, NodeExecutionState>

  // ── Actions ──────────────────────────────────────────────────────────
  fetchWorkflows: () => Promise<void>
  fetchWorkflow: (id: string) => Promise<void>
  createWorkflow: (req: WorkflowCreateRequest) => Promise<WorkflowConfig>
  updateWorkflow: (id: string, req: WorkflowUpdateRequest) => Promise<WorkflowConfig>
  deleteWorkflow: (id: string) => Promise<void>

  setCurrentWorkflow: (wf: WorkflowConfig | null) => void
  updateNodes: (nodes: WorkflowNodeConfig[]) => void
  updateEdges: (edges: WorkflowEdgeConfig[]) => void
  updateNode: (node: WorkflowNodeConfig) => void

  executeWorkflow: (id: string, input: string) => AbortController
  resetExecution: () => void
}

export const useWorkflowStore = create<WorkflowState>()((set, get) => ({
  // ── 初始状态 ───────────────────────────────────────────────────────────
  workflows: [],
  loading: false,
  error: null,
  currentWorkflow: null,
  executionId: null,
  executionRunning: false,
  executionOutput: '',
  nodeStates: {},

  // ── 列表操作 ───────────────────────────────────────────────────────────
  fetchWorkflows: async () => {
    set({ loading: true, error: null })
    try {
      const workflows = await listWorkflows()
      set({ workflows, loading: false })
    } catch (err) {
      set({ error: String(err), loading: false })
    }
  },

  fetchWorkflow: async (id) => {
    set({ loading: true, error: null })
    try {
      const wf = await getWorkflow(id)
      set({ currentWorkflow: wf, loading: false })
    } catch (err) {
      set({ error: String(err), loading: false })
    }
  },

  createWorkflow: async (req) => {
    const created = await createWorkflow(req)
    set((s) => ({ workflows: [...s.workflows, created] }))
    return created
  },

  updateWorkflow: async (id, req) => {
    const updated = await updateWorkflow(id, req)
    set((s) => ({
      workflows: s.workflows.map((w) => (w.id === id ? updated : w)),
      currentWorkflow: s.currentWorkflow?.id === id ? updated : s.currentWorkflow,
    }))
    return updated
  },

  deleteWorkflow: async (id) => {
    await deleteWorkflow(id)
    set((s) => ({
      workflows: s.workflows.filter((w) => w.id !== id),
      currentWorkflow: s.currentWorkflow?.id === id ? null : s.currentWorkflow,
    }))
  },

  // ── 画布操作 ───────────────────────────────────────────────────────────
  setCurrentWorkflow: (wf) => set({ currentWorkflow: wf }),

  updateNodes: (nodes) => {
    set((s) =>
      s.currentWorkflow
        ? { currentWorkflow: { ...s.currentWorkflow, nodes } }
        : {},
    )
  },

  updateEdges: (edges) => {
    set((s) =>
      s.currentWorkflow
        ? { currentWorkflow: { ...s.currentWorkflow, edges } }
        : {},
    )
  },

  updateNode: (node) => {
    set((s) => {
      if (!s.currentWorkflow) return {}
      const nodes = s.currentWorkflow.nodes.map((n) =>
        n.nodeId === node.nodeId ? node : n,
      )
      return { currentWorkflow: { ...s.currentWorkflow, nodes } }
    })
  },

  // ── 执行 ───────────────────────────────────────────────────────────────
  executeWorkflow: (id, input) => {
    const { resetExecution } = get()
    resetExecution()
    set({ executionRunning: true })

    const handleChunk = (chunk: SseChunk) => {
      switch (chunk.type) {
        case 'workflow_start':
          set({ executionId: chunk.executionId })
          break

        case 'workflow_node_start':
          set((s) => ({
            nodeStates: {
              ...s.nodeStates,
              [chunk.nodeId]: { status: 'running' },
            },
          }))
          break

        case 'workflow_node_complete':
          set((s) => ({
            nodeStates: {
              ...s.nodeStates,
              [chunk.nodeId]: {
                status: 'completed',
                result: chunk.result,
                durationMs: chunk.durationMs,
              },
            },
          }))
          break

        case 'workflow_error':
          set((s) => ({
            nodeStates: {
              ...s.nodeStates,
              [chunk.nodeId]: { status: 'error', error: chunk.error },
            },
          }))
          break

        case 'workflow_complete':
          set({ executionOutput: chunk.finalResult })
          break

        case 'token':
          // 工作流执行时 token 来自子 Agent，追加到输出
          set((s) => ({ executionOutput: s.executionOutput + chunk.content }))
          break

        default:
          break
      }
    }

    const controller = streamWorkflow(
      id,
      input,
      handleChunk,
      (err) => {
        set({ executionRunning: false, error: err })
      },
      () => {
        set({ executionRunning: false })
      },
    )

    return controller
  },

  resetExecution: () =>
    set({
      executionId: null,
      executionRunning: false,
      executionOutput: '',
      nodeStates: {},
      error: null,
    }),
}))
