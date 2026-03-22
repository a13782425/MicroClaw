<template>
  <div class="page-container">
    <div class="page-header">
      <div class="header-left">
        <h2 class="page-title">定时任务</h2>
        <p class="page-desc">配置 AI 定时触发任务，任务到期时向指定会话发送提示并获取回复</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="openCreate">新建任务</el-button>
    </div>

    <el-table :data="jobs" v-loading="loading" stripe border style="width:100%">
      <el-table-column label="任务名称" prop="name" min-width="140">
        <template #default="{ row }">
          <div class="job-name">
            <span>{{ row.name }}</span>
            <el-tag v-if="row.description" size="small" type="info" effect="plain" class="desc-tag">
              {{ row.description }}
            </el-tag>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Cron 表达式" prop="cronExpression" width="160">
        <template #default="{ row }">
          <code class="cron-code">{{ row.cronExpression }}</code>
        </template>
      </el-table-column>
      <el-table-column label="触发提示词" prop="prompt" min-width="180" show-overflow-tooltip />
      <el-table-column label="目标会话" width="160" show-overflow-tooltip>
        <template #default="{ row }">
          <span class="session-id">{{ row.targetSessionId }}</span>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'danger'" size="small">
            {{ row.isEnabled ? '已启用' : '已禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="上次执行" width="160">
        <template #default="{ row }">
          <span class="time-text">{{ row.lastRunAtUtc ? formatTime(row.lastRunAtUtc) : '—' }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="280" align="center">
        <template #default="{ row }">
          <el-button size="small" @click="openEdit(row)">编辑</el-button>
          <el-button size="small" :type="row.isEnabled ? 'warning' : 'success'" @click="handleToggle(row)">
            {{ row.isEnabled ? '禁用' : '启用' }}
          </el-button>
          <el-button
            size="small"
            type="primary"
            plain
            :loading="triggeringId === row.id"
            @click="handleTrigger(row)"
          >触发</el-button>
          <el-button size="small" plain @click="openLogs(row)">日志</el-button>
          <el-popconfirm title="确认删除此任务？" @confirm="handleDelete(row.id)">
            <template #reference>
              <el-button size="small" type="danger">删除</el-button>
            </template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-empty v-if="!loading && jobs.length === 0" description="暂无定时任务，点击「新建任务」创建" :image-size="90" />

    <!-- 创建/编辑对话框 -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingJob ? '编辑定时任务' : '新建定时任务'"
      width="560px"
      :close-on-click-modal="false"
      draggable
    >
      <el-form :model="form" :rules="rules" ref="formRef" label-width="110px" class="job-form">
        <el-form-item label="任务名称" prop="name">
          <el-input v-model="form.name" placeholder="如：每日早报、周报提醒" />
        </el-form-item>
        <el-form-item label="任务描述" prop="description">
          <el-input v-model="form.description" placeholder="可选，简单描述任务用途" />
        </el-form-item>
        <el-form-item label="Cron 表达式" prop="cronExpression">
          <el-input v-model="form.cronExpression" placeholder="如：0 0 9 * * ?" />
          <div class="form-hint">
            Quartz 格式（秒 分 时 日 月 周）：<br />
            <code>0 0 9 * * ?</code> 每天9点 &nbsp;｜&nbsp;
            <code>0 30 8 ? * MON-FRI</code> 工作日8:30
          </div>
        </el-form-item>
        <el-form-item label="触发提示词" prop="prompt">
          <el-input
            v-model="form.prompt"
            type="textarea"
            :rows="3"
            placeholder="任务触发时发送给 AI 的提示词，AI 会生成回复保存到目标会话"
          />
        </el-form-item>
        <el-form-item label="目标会话 ID" prop="targetSessionId">
          <el-input v-model="form.targetSessionId" placeholder="会话 ID（可在会话列表获取）" />
          <div class="form-hint">AI 回复将保存到该会话，并通过 SignalR 实时推送</div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 执行历史日志 Drawer -->
    <el-drawer
      v-model="logsDrawerVisible"
      :title="`执行日志：${logsJob?.name ?? ''}`"
      size="600px"
      direction="rtl"
    >
      <div v-if="logsLoading" class="logs-loading">
        <el-icon class="rotating"><Loading /></el-icon>
        <span>加载中…</span>
      </div>
      <template v-else>
        <div v-if="logs.length === 0" class="logs-empty">暂无执行记录</div>
        <el-timeline v-else>
          <el-timeline-item
            v-for="log in logs"
            :key="log.id"
            :timestamp="formatTime(log.triggeredAtUtc)"
            placement="top"
            :type="log.status === 'success' ? 'success' : log.status === 'cancelled' ? 'warning' : 'danger'"
          >
            <div class="log-item">
              <div class="log-header">
                <el-tag
                  :type="log.status === 'success' ? 'success' : log.status === 'cancelled' ? 'warning' : 'danger'"
                  size="small"
                >{{ statusLabel(log.status) }}</el-tag>
                <el-tag type="info" size="small" effect="plain">{{ sourceLabel(log.source) }}</el-tag>
                <span class="log-duration">{{ log.durationMs }} ms</span>
              </div>
              <div v-if="log.errorMessage" class="log-error">{{ log.errorMessage }}</div>
            </div>
          </el-timeline-item>
        </el-timeline>
      </template>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Plus, Loading } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { cronApi, type CronJob, type CronJobRunLog } from '@/services/cronApi'

const jobs = ref<CronJob[]>([])
const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingJob = ref<CronJob | null>(null)
const formRef = ref<FormInstance>()

// 手动触发
const triggeringId = ref<string | null>(null)

// 执行历史日志
const logsDrawerVisible = ref(false)
const logsJob = ref<CronJob | null>(null)
const logs = ref<CronJobRunLog[]>([])
const logsLoading = ref(false)

const defaultForm = () => ({
  name: '',
  description: '',
  cronExpression: '',
  prompt: '',
  targetSessionId: '',
})

const form = ref(defaultForm())

const rules: FormRules = {
  name: [{ required: true, message: '请输入任务名称', trigger: 'blur' }],
  cronExpression: [{ required: true, message: '请输入 Cron 表达式', trigger: 'blur' }],
  prompt: [{ required: true, message: '请输入触发提示词', trigger: 'blur' }],
  targetSessionId: [{ required: true, message: '请输入目标会话 ID', trigger: 'blur' }],
}

async function loadJobs() {
  loading.value = true
  try {
    jobs.value = await cronApi.list()
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingJob.value = null
  form.value = defaultForm()
  dialogVisible.value = true
  formRef.value?.clearValidate()
}

function openEdit(job: CronJob) {
  editingJob.value = job
  form.value = {
    name: job.name,
    description: job.description ?? '',
    cronExpression: job.cronExpression,
    prompt: job.prompt,
    targetSessionId: job.targetSessionId,
  }
  dialogVisible.value = true
  formRef.value?.clearValidate()
}

async function handleSave() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    if (editingJob.value) {
      const updated = await cronApi.update({
        id: editingJob.value.id,
        name: form.value.name,
        description: form.value.description || null,
        cronExpression: form.value.cronExpression,
        prompt: form.value.prompt,
        targetSessionId: form.value.targetSessionId,
      })
      const idx = jobs.value.findIndex(j => j.id === updated.id)
      if (idx >= 0) jobs.value[idx] = updated
      ElMessage.success('任务已更新')
    } else {
      const created = await cronApi.create({
        name: form.value.name,
        description: form.value.description || null,
        cronExpression: form.value.cronExpression,
        prompt: form.value.prompt,
        targetSessionId: form.value.targetSessionId,
      })
      jobs.value.unshift(created)
      ElMessage.success('任务已创建')
    }
    dialogVisible.value = false
  } catch {
    // 失败由全局拦截器展示后端错误信息
  } finally {
    saving.value = false
  }
}

async function handleToggle(job: CronJob) {
  try {
    const updated = await cronApi.toggle(job.id)
    const idx = jobs.value.findIndex(j => j.id === updated.id)
    if (idx >= 0) jobs.value[idx] = updated
    ElMessage.success(updated.isEnabled ? '任务已启用' : '任务已禁用')
  } catch {
    // 失败由全局拦截器展示后端错误信息
  }
}

async function handleDelete(id: string) {
  try {
    await cronApi.delete(id)
    jobs.value = jobs.value.filter(j => j.id !== id)
    ElMessage.success('任务已删除')
  } catch {
    // 失败由全局拦截器展示后端错误信息
  }
}

async function handleTrigger(job: CronJob) {
  triggeringId.value = job.id
  try {
    const result = await cronApi.trigger(job.id)
    if (result.success) {
      ElMessage.success(`触发成功，用时 ${result.durationMs} ms`)
    } else {
      ElMessage.warning(`触发结果：${result.status}${result.errorMessage ? ' — ' + result.errorMessage : ''}`)
    }
    // 刷新 lastRunAtUtc
    const updated = await cronApi.list()
    jobs.value = updated
  } catch {
    // 失败由全局拦截器展示后端错误信息
  } finally {
    triggeringId.value = null
  }
}

async function openLogs(job: CronJob) {
  logsJob.value = job
  logsDrawerVisible.value = true
  logsLoading.value = true
  try {
    logs.value = await cronApi.getLogs(job.id, 50)
  } catch {
    logs.value = []
  } finally {
    logsLoading.value = false
  }
}

function statusLabel(status: string): string {
  return status === 'success' ? '成功' : status === 'cancelled' ? '已取消' : '失败'
}

function sourceLabel(source: string): string {
  return source === 'manual' ? '手动' : '自动'
}

function formatTime(isoStr: string): string {
  const d = new Date(isoStr)
  return d.toLocaleString('zh-CN', { hour12: false })
}

onMounted(loadJobs)
</script>

<style scoped>
.page-container {
  max-width: 1200px;
  height: 100%;
  padding: 24px;
  overflow-y: auto;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 24px;
}

.header-left {
  flex: 1;
}

.page-title {
  margin: 0 0 4px;
  font-size: 22px;
  font-weight: 700;
  color: #1f2937;
}

.page-desc {
  margin: 0;
  font-size: 14px;
  color: #6b7280;
}

.job-name {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.desc-tag {
  max-width: 100px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.cron-code {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  background: #f3f4f6;
  padding: 2px 6px;
  border-radius: 4px;
  color: #6366f1;
}

.session-id {
  font-family: monospace;
  font-size: 12px;
  color: #6b7280;
}

.time-text {
  font-size: 13px;
  color: #374151;
}

.job-form .form-hint {
  color: #9ca3af;
  font-size: 12px;
  margin-top: 4px;
  line-height: 1.6;
}

.job-form .form-hint code {
  background: #f3f4f6;
  padding: 1px 4px;
  border-radius: 3px;
  font-family: monospace;
  color: #6366f1;
}

/* 执行历史日志 */
.logs-loading {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 24px;
  color: #6b7280;
}

.logs-empty {
  padding: 48px;
  text-align: center;
  color: #9ca3af;
  font-size: 14px;
}

.log-item {
  padding: 4px 0;
}

.log-header {
  display: flex;
  align-items: center;
  gap: 6px;
}

.log-duration {
  font-size: 12px;
  color: #6b7280;
}

.log-error {
  margin-top: 4px;
  font-size: 12px;
  color: #ef4444;
  word-break: break-all;
}

.rotating {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>

