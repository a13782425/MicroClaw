import request from '../request'

export type CronJob = {
  id: string
  name: string
  description: string | null
  cronExpression: string
  targetSessionId: string
  prompt: string
  isEnabled: boolean
  createdAtUtc: string
  lastRunAtUtc: string | null
}

export type CronJobRunLog = {
  id: string
  cronJobId: string
  triggeredAtUtc: string
  /** success / failed / cancelled */
  status: string
  durationMs: number
  errorMessage: string | null
  /** cron（自动）/ manual（手动） */
  source: string
}

export type TriggerResult = {
  success: boolean
  status: string
  durationMs: number
  errorMessage: string | null
}

export type CreateCronJobRequest = {
  name: string
  description?: string | null
  cronExpression: string
  targetSessionId: string
  prompt: string
}

export type UpdateCronJobRequest = {
  id: string
  name?: string | null
  description?: string | null
  cronExpression?: string | null
  targetSessionId?: string | null
  prompt?: string | null
  isEnabled?: boolean | null
}

export const cronApi = {
  list(): Promise<CronJob[]> {
    return request.get('/api/cron').then((r) => r.data as CronJob[])
  },
  create(req: CreateCronJobRequest): Promise<CronJob> {
    return request.post('/api/cron', req).then((r) => r.data as CronJob)
  },
  update(req: UpdateCronJobRequest): Promise<CronJob> {
    return request.post('/api/cron/update', req).then((r) => r.data as CronJob)
  },
  delete(id: string): Promise<void> {
    return request.post('/api/cron/delete', { id }).then(() => undefined)
  },
  toggle(id: string): Promise<CronJob> {
    return request.post('/api/cron/toggle', { id }).then((r) => r.data as CronJob)
  },
  trigger(id: string): Promise<TriggerResult> {
    return request.post('/api/cron/trigger', { id }).then((r) => r.data as TriggerResult)
  },
  getLogs(id: string, limit = 50): Promise<CronJobRunLog[]> {
    return request.get(`/api/cron/${id}/logs`, { params: { limit } }).then((r) => r.data as CronJobRunLog[])
  },
}
