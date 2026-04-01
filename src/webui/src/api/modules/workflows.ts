import request from '../request'
import { useAuthStore } from '@/store/authStore'
import type { SseChunk } from './shared'

export type { SseChunk } from './shared'

export type WorkflowNodeType = 'Agent' | 'Function' | 'Tool' | 'Router' | 'SwitchModel' | 'Start' | 'End'

export type WorkflowPosition = {
  x: number
  y: number
}

export type WorkflowNodeConfig = {
  nodeId: string
  label: string
  type: WorkflowNodeType
  agentId?: string | null
  functionName?: string | null
  providerId?: string | null
  config?: Record<string, string> | null
  position?: WorkflowPosition | null
}

export type WorkflowEdgeConfig = {
  sourceNodeId: string
  targetNodeId: string
  condition?: string | null
  label?: string | null
}

export type WorkflowConfig = {
  id: string
  name: string
  description: string
  isEnabled: boolean
  nodes: WorkflowNodeConfig[]
  edges: WorkflowEdgeConfig[]
  entryNodeId?: string | null
  defaultProviderId?: string | null
  createdAt: string
  updatedAt: string
}

export type WorkflowCreateRequest = {
  name: string
  description?: string
  isEnabled?: boolean
  nodes?: WorkflowNodeConfig[]
  edges?: WorkflowEdgeConfig[]
  entryNodeId?: string
  defaultProviderId?: string | null
}

export type WorkflowUpdateRequest = {
  name?: string
  description?: string
  isEnabled?: boolean
  nodes?: WorkflowNodeConfig[]
  edges?: WorkflowEdgeConfig[]
  entryNodeId?: string
  defaultProviderId?: string | null
}

export async function listWorkflows(): Promise<WorkflowConfig[]> {
  const { data } = await request.get<WorkflowConfig[]>('/api/workflows')
  return data
}

export async function getWorkflow(id: string): Promise<WorkflowConfig> {
  const { data } = await request.get<WorkflowConfig>(`/api/workflows/${id}`)
  return data
}

export async function createWorkflow(req: WorkflowCreateRequest): Promise<WorkflowConfig> {
  const { data } = await request.post<WorkflowConfig>('/api/workflows', req)
  return data
}

export async function updateWorkflow(id: string, req: WorkflowUpdateRequest): Promise<WorkflowConfig> {
  const { data } = await request.put<WorkflowConfig>(`/api/workflows/${id}`, req)
  return data
}

export async function deleteWorkflow(id: string): Promise<void> {
  await request.delete(`/api/workflows/${id}`)
}

export function streamWorkflow(
  workflowId: string,
  input: string,
  onChunk: (chunk: SseChunk) => void,
  onError: (err: string) => void,
  onDone: () => void,
): AbortController {
  const controller = new AbortController()
  const token = useAuthStore.getState().token

  fetch(`/api/workflows/${workflowId}/execute`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ input }),
    signal: controller.signal,
  })
    .then(async (res) => {
      if (!res.ok) {
        onError(`HTTP ${res.status}`)
        return
      }
      const reader = res.body?.getReader()
      if (!reader) {
        onError('No response body')
        return
      }
      const decoder = new TextDecoder()
      let buffer = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const parts = buffer.split('\n\n')
        buffer = parts.pop() ?? ''
        for (const part of parts) {
          const raw = part.replace(/^data: /, '').trim()
          if (!raw) continue
          if (raw === '[DONE]') {
            onDone()
            return
          }
          try {
            const chunk = JSON.parse(raw) as SseChunk
            onChunk(chunk)
          } catch {
            // Ignore malformed SSE payloads.
          }
        }
      }
      onDone()
    })
    .catch((err) => {
      if (err?.name !== 'AbortError') {
        onError(String(err))
      }
    })

  return controller
}