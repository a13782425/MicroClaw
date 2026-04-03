import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Table, Switch, Dialog,
  createListCollection, Select, Portal,
} from '@chakra-ui/react'
import { Plus, Trash2, Edit, Play, Clock, FileText, RefreshCw } from 'lucide-react'
import {
  cronApi,
  listSessions,
  type CronJob,
  type CronJobRunLog,
  type CreateCronJobRequest,
  type SessionInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { eventBus } from '@/services/eventBus'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

function formatDate(d: string | null | undefined): string {
  if (!d) return '—'
  return new Date(d).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}

// ─── 日志弹窗 ──────────────────────────────────────────────────────────────────

function LogDialog({ jobId, jobName, open, onClose }: { jobId: string; jobName: string; open: boolean; onClose: () => void }) {
  const [logs, setLogs] = useState<CronJobRunLog[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (open && jobId) {
      setLoading(true)
      cronApi.getLogs(jobId).then(setLogs).catch(() => toaster.create({ type: 'error', title: '加载日志失败' })).finally(() => setLoading(false))
    }
  }, [open, jobId])

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) onClose() }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="640px">
          <Dialog.Header><Dialog.Title>执行日志 — {jobName}</Dialog.Title></Dialog.Header>
          <Dialog.Body maxH="480px" overflow="auto">
            {loading && <Spinner />}
            {!loading && logs.length === 0 && <Text color="var(--mc-text-muted)" textAlign="center" py="4">暂无执行记录</Text>}
            {!loading && logs.map((log) => (
              <Box key={log.id} mb="3" p="3" borderWidth="1px" rounded="md">
                <HStack mb="1" justify="space-between">
                  <HStack>
                    <Badge size="sm" colorPalette={log.status === 'success' ? 'green' : 'red'}>{log.status}</Badge>
                    <Badge size="sm" variant="outline">{log.source}</Badge>
                  </HStack>
                  <Text fontSize="xs" color="var(--mc-text-muted)">{formatDate(log.triggeredAtUtc)} · {log.durationMs}ms</Text>
                </HStack>
                {log.errorMessage && <Text fontSize="xs" color="red.500" fontFamily="mono" whiteSpace="pre-wrap">{log.errorMessage}</Text>}
              </Box>
            ))}
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>关闭</Button>
          </Dialog.Footer>
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ─── 编辑/新建弹窗 ─────────────────────────────────────────────────────────────

interface CronDialogProps {
  open: boolean
  editing: CronJob | null
  sessions: SessionInfo[]
  onClose: () => void
  onSaved: () => void
}

function CronDialog({ open, editing, sessions, onClose, onSaved }: CronDialogProps) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [cronExpr, setCronExpr] = useState('')
  const [sessionId, setSessionId] = useState('')
  const [prompt, setPrompt] = useState('')
  const [saving, setSaving] = useState(false)

  const sessionCollection = createListCollection({
    items: sessions.map((s) => ({ value: s.id, label: s.title })),
  })

  useEffect(() => {
    if (open) {
      if (editing) {
        setName(editing.name)
        setDescription(editing.description ?? '')
        setCronExpr(editing.cronExpression)
        setSessionId(editing.targetSessionId)
        setPrompt(editing.prompt)
      } else {
        setName(''); setDescription(''); setCronExpr(''); setSessionId(''); setPrompt('')
      }
    }
  }, [open, editing])

  const handleSave = async () => {
    if (!name.trim() || !cronExpr.trim() || !sessionId || !prompt.trim()) {
      toaster.create({ type: 'error', title: '请填写所有必填字段' })
      return
    }
    setSaving(true)
    try {
      if (editing) {
        await cronApi.update({ id: editing.id, name: name.trim(), description: description.trim() || null, cronExpression: cronExpr.trim(), targetSessionId: sessionId, prompt: prompt.trim() })
      } else {
        const req: CreateCronJobRequest = { name: name.trim(), description: description.trim() || null, cronExpression: cronExpr.trim(), targetSessionId: sessionId, prompt: prompt.trim() }
        await cronApi.create(req)
      }
      toaster.create({ type: 'success', title: editing ? '任务已更新' : '任务已创建' })
      onSaved()
      onClose()
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) onClose() }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="520px">
          <Dialog.Header><Dialog.Title>{editing ? '编辑任务' : '新建计划任务'}</Dialog.Title></Dialog.Header>
          <Dialog.Body>
            <VStack gap="4" align="stretch">
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">名称 <Text as="span" color="red.500">*</Text></Text>
                <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="任务名称" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">描述</Text>
                <Textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="任务描述（可选）" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">Cron 表达式 <Text as="span" color="red.500">*</Text></Text>
                <Input value={cronExpr} onChange={(e) => setCronExpr(e.target.value)} placeholder="如 0 9 * * * （每天9点）" fontFamily="mono" />
                <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">格式：秒 分 时 日 月 周（6 段）</Text>
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">目标会话 <Text as="span" color="red.500">*</Text></Text>
                {sessions.length === 0 ? (
                  <Text fontSize="sm" color="var(--mc-text-muted)">暂无可用会话</Text>
                ) : (
                  <Select.Root collection={sessionCollection} value={sessionId ? [sessionId] : []} onValueChange={(e) => setSessionId(e.value[0])}>
                    <Select.HiddenSelect />
                    <Select.Control><Select.Trigger><Select.ValueText placeholder="选择会话" /></Select.Trigger><Select.IndicatorGroup><Select.Indicator /></Select.IndicatorGroup></Select.Control>
                    <Portal><Select.Positioner><Select.Content>
                      {sessions.map((s) => <Select.Item key={s.id} item={{ value: s.id, label: s.title }}>{s.title}</Select.Item>)}
                    </Select.Content></Select.Positioner></Portal>
                  </Select.Root>
                )}
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">提示词 <Text as="span" color="red.500">*</Text></Text>
                <Textarea rows={4} value={prompt} onChange={(e) => setPrompt(e.target.value)} placeholder="每次执行时发送给 AI 的提示词" />
              </Box>
            </VStack>
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>取消</Button>
            <Button colorPalette="blue" loading={saving} onClick={handleSave} disabled={!name.trim() || !cronExpr.trim() || !sessionId || !prompt.trim()}>保存</Button>
          </Dialog.Footer>
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function CronPage() {
  const [jobs, setJobs] = useState<CronJob[]>([])
  const [sessions, setSessions] = useState<SessionInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<CronJob | null>(null)
  const [logJob, setLogJob] = useState<CronJob | null>(null)
  const [triggering, setTriggering] = useState<Record<string, boolean>>({})
  const [deleteTarget, setDeleteTarget] = useState<CronJob | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [jobsData, sessionsData] = await Promise.all([cronApi.list(), listSessions()])
      setJobs(jobsData)
      setSessions(sessionsData)
    } catch {
      toaster.create({ type: 'error', title: '加载任务列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  // SignalR 事件联动
  useEffect(() => {
    const handler = (...args: unknown[]) => {
      const data = args[0] as { jobName: string; success: boolean; message?: string }
      toaster.create({
        type: data.success ? 'success' : 'error',
        title: `定时任务「${data.jobName}」${data.success ? '执行成功' : '执行失败'}`,
        description: data.message,
      })
      load()
    }
    eventBus.on('cron:jobExecuted', handler)
    return () => eventBus.off('cron:jobExecuted', handler)
  }, [load])

  const handleToggle = async (job: CronJob, val: boolean) => {
    try {
      const updated = await cronApi.toggle(job.id)
      setJobs((prev) => prev.map((j) => j.id === job.id ? updated : j))
    } catch {
      toaster.create({ type: 'error', title: '切换失败' })
    }
  }

  const handleDelete = async (job: CronJob) => {
    try {
      await cronApi.delete(job.id)
      toaster.create({ type: 'success', title: '任务已删除' })
      setJobs((prev) => prev.filter((j) => j.id !== job.id))
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteTarget(null)
    }
  }

  const handleTrigger = async (job: CronJob) => {
    setTriggering((prev) => ({ ...prev, [job.id]: true }))
    try {
      const res = await cronApi.trigger(job.id)
      toaster.create({ type: res.success ? 'success' : 'error', title: res.success ? '手动触发成功' : `执行失败：${res.errorMessage}` })
      load()
    } catch {
      toaster.create({ type: 'error', title: '触发失败' })
    } finally {
      setTriggering((prev) => ({ ...prev, [job.id]: false }))
    }
  }

  const getSessionName = (id: string) => sessions.find((s) => s.id === id)?.title ?? id

  return (
    <Box p="6">
      <HStack mb="4" justify="space-between">
        <Text fontWeight="semibold" fontSize="lg">计划任务</Text>
        <HStack>
          <Button size="sm" variant="outline" loading={loading} onClick={load}><RefreshCw size={14} /></Button>
          <Button size="sm" colorPalette="blue" onClick={() => { setEditing(null); setDialogOpen(true) }}>
            <Plus size={14} />新建任务
          </Button>
        </HStack>
      </HStack>

      {loading && <Box py="8" textAlign="center"><Spinner /></Box>}
      {!loading && jobs.length === 0 && (
        <Box py="8" textAlign="center" color="var(--mc-text-muted)">暂无计划任务，点击「新建任务」添加</Box>
      )}

      {!loading && jobs.length > 0 && (
        <Table.Root variant="outline">
          <Table.Header>
            <Table.Row>
              <Table.ColumnHeader>名称</Table.ColumnHeader>
              <Table.ColumnHeader>Cron 表达式</Table.ColumnHeader>
              <Table.ColumnHeader>目标会话</Table.ColumnHeader>
              <Table.ColumnHeader>上次执行</Table.ColumnHeader>
              <Table.ColumnHeader>启用</Table.ColumnHeader>
              <Table.ColumnHeader>操作</Table.ColumnHeader>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {jobs.map((job) => (
              <Table.Row key={job.id}>
                <Table.Cell>
                  <VStack gap="0" align="start">
                    <Text fontWeight="medium" fontSize="sm">{job.name}</Text>
                    {job.description && <Text fontSize="xs" color="var(--mc-text-muted)" truncate maxW="180px">{job.description}</Text>}
                  </VStack>
                </Table.Cell>
                <Table.Cell fontFamily="mono" fontSize="xs">{job.cronExpression}</Table.Cell>
                <Table.Cell fontSize="xs">{getSessionName(job.targetSessionId)}</Table.Cell>
                <Table.Cell fontSize="xs" color="var(--mc-text-muted)">{formatDate(job.lastRunAtUtc)}</Table.Cell>
                <Table.Cell>
                  <Switch.Root size="sm" checked={job.isEnabled} onCheckedChange={(e) => handleToggle(job, e.checked)}>
                    <Switch.HiddenInput />
                    <Switch.Control><Switch.Thumb /></Switch.Control>
                  </Switch.Root>
                </Table.Cell>
                <Table.Cell>
                  <HStack gap="1">
                    <Button size="xs" variant="ghost" title="手动触发" loading={triggering[job.id]} onClick={() => handleTrigger(job)}>
                      <Play size={12} />
                    </Button>
                    <Button size="xs" variant="ghost" title="查看日志" onClick={() => setLogJob(job)}>
                      <FileText size={12} />
                    </Button>
                    <Button size="xs" variant="ghost" title="编辑" onClick={() => { setEditing(job); setDialogOpen(true) }}>
                      <Edit size={12} />
                    </Button>
                    <Button size="xs" variant="ghost" colorPalette="red" title="删除" onClick={() => setDeleteTarget(job)}>
                      <Trash2 size={12} />
                    </Button>
                  </HStack>
                </Table.Cell>
              </Table.Row>
            ))}
          </Table.Body>
        </Table.Root>
      )}

      <CronDialog open={dialogOpen} editing={editing} sessions={sessions} onClose={() => setDialogOpen(false)} onSaved={load} />
      {logJob && <LogDialog open={!!logJob} jobId={logJob.id} jobName={logJob.name} onClose={() => setLogJob(null)} />}

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除任务"
        description={`确认删除任务「${deleteTarget?.name}」？`}
        confirmText="删除"
      />
    </Box>
  )
}
