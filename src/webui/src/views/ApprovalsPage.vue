<template>
  <div class="approvals-page">
    <div class="page-header">
      <h2 class="page-title">会话审批</h2>
      <div class="header-actions">
        <el-button
          type="success"
          :icon="Check"
          size="small"
          :disabled="selected.length === 0"
          :loading="batchApproving"
          @click="handleBatchApprove"
        >批量批准{{ selected.length ? ` (${selected.length})` : '' }}</el-button>
        <el-button
          type="warning"
          :icon="CircleClose"
          size="small"
          :disabled="selected.length === 0"
          :loading="batchDisabling"
          @click="handleBatchDisable"
        >批量禁用{{ selected.length ? ` (${selected.length})` : '' }}</el-button>
        <el-select
          v-model="statusFilter"
          size="small"
          style="width: 110px"
          placeholder="状态筛选"
        >
          <el-option label="全部" value="all" />
          <el-option label="待审批" value="pending" />
          <el-option label="已批准" value="approved" />
        </el-select>
        <el-button :icon="Refresh" circle size="small" @click="refresh" :loading="refreshing" title="刷新" />
      </div>
    </div>

    <div v-if="loading" class="loading-wrap">
      <el-skeleton :rows="6" animated />
    </div>

    <el-table
      v-else
      :data="filteredSessions"
      @selection-change="onSelectionChange"
      row-key="id"
      stripe
      class="approvals-table"
    >
      <el-table-column type="selection" width="50" reserve-selection />

      <el-table-column label="会话名称" min-width="180">
        <template #default="{ row }">
          <span class="session-title">{{ row.title }}</span>
        </template>
      </el-table-column>

      <el-table-column label="状态" width="96">
        <template #default="{ row }">
          <el-tag
            :type="row.isApproved ? 'success' : 'warning'"
            effect="plain"
            size="small"
          >{{ row.isApproved ? '已批准' : '待审批' }}</el-tag>
        </template>
      </el-table-column>

      <el-table-column label="渠道" width="80">
        <template #default="{ row }">{{ channelLabel(row.channelType) }}</template>
      </el-table-column>

      <el-table-column label="模型" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ providerName(row.providerId) }}</template>
      </el-table-column>

      <el-table-column label="创建时间" width="120">
        <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
      </el-table-column>

      <el-table-column label="审批原因" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">
          <span v-if="row.approvalReason" class="reason-text">{{ row.approvalReason }}</span>
          <span v-else class="reason-empty">—</span>
        </template>
      </el-table-column>

      <el-table-column label="操作" width="140" fixed="right">
        <template #default="{ row }">
          <el-button
            v-if="!row.isApproved"
            type="success"
            :icon="Check"
            size="small"
            @click="handleApprove(row)"
          >批准</el-button>
          <el-button
            v-else
            type="warning"
            :icon="CircleClose"
            size="small"
            @click="handleDisable(row)"
          >禁用</el-button>
        </template>
      </el-table-column>

      <template #empty>
        <el-empty :description="statusFilter === 'all' ? '暂无会话' : '无匹配会话'" />
      </template>
    </el-table>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import {
  listSessions,
  listProviders,
  approveSession,
  disableSession,
  batchApproveSession,
  batchDisableSession,
  type SessionInfo,
  type ProviderConfig,
} from '@/services/gatewayApi'
import {
  Check, CircleClose, Refresh,
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { eventBus } from '@/services/eventBus'

const sessions = ref<SessionInfo[]>([])
const providers = ref<ProviderConfig[]>([])
const loading = ref(false)
const refreshing = ref(false)
const batchApproving = ref(false)
const batchDisabling = ref(false)
const selected = ref<SessionInfo[]>([])
const statusFilter = ref<'all' | 'pending' | 'approved'>('all')

const filteredSessions = computed(() => {
  if (statusFilter.value === 'pending') return sessions.value.filter((s) => !s.isApproved)
  if (statusFilter.value === 'approved') return sessions.value.filter((s) => s.isApproved)
  return sessions.value
})

const channelLabelMap: Record<string, string> = {
  web: 'Web',
  feishu: '飞书',
  wecom: '企微',
  wechat: '微信',
}

function channelLabel(type: string): string {
  if (!type || type === 'web') return 'Web'
  return channelLabelMap[type] ?? type
}

function providerName(id: string): string {
  const p = providers.value.find((item) => item.id === id)
  return p ? `${p.displayName} (${p.modelName})` : id
}

function formatTime(iso: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  return d.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function onSelectionChange(rows: SessionInfo[]) {
  selected.value = rows
}

async function fetchSessions() {
  const [s, p] = await Promise.all([listSessions(), listProviders()])
  sessions.value = s
  providers.value = p
}

async function refresh() {
  refreshing.value = true
  try {
    await fetchSessions()
  } finally {
    refreshing.value = false
  }
}

/** 弹出原因输入框，返回输入的原因（可为空字符串）；取消则返回 null */
async function promptReason(title: string, confirmText: string): Promise<string | null> {
  try {
    const { value } = await ElMessageBox.prompt(
      '可输入原因（可选，留空跳过）',
      title,
      {
        confirmButtonText: confirmText,
        cancelButtonText: '取消',
        inputPlaceholder: '请输入原因说明…',
        inputType: 'textarea',
        inputValue: '',
      },
    )
    return value ?? ''
  } catch {
    return null
  }
}

async function handleApprove(row: SessionInfo) {
  const reason = await promptReason(`批准会话「${row.title}」`, '确认批准')
  if (reason === null) return
  try {
    const updated = await approveSession(row.id, reason || undefined)
    const idx = sessions.value.findIndex((s) => s.id === row.id)
    if (idx >= 0) sessions.value[idx] = updated
    ElMessage.success('会话已批准')
  } catch {
    // 全局拦截器处理
  }
}

async function handleDisable(row: SessionInfo) {
  const reason = await promptReason(`禁用会话「${row.title}」`, '确认禁用')
  if (reason === null) return
  try {
    const updated = await disableSession(row.id, reason || undefined)
    const idx = sessions.value.findIndex((s) => s.id === row.id)
    if (idx >= 0) sessions.value[idx] = updated
    ElMessage.success('会话已禁用')
  } catch {
    // 全局拦截器处理
  }
}

async function handleBatchApprove() {
  if (selected.value.length === 0) return
  const reason = await promptReason(`批量批准 ${selected.value.length} 个会话`, '确认批准')
  if (reason === null) return
  batchApproving.value = true
  try {
    const { updated } = await batchApproveSession(selected.value.map((s) => s.id), reason || undefined)
    for (const u of updated) {
      const idx = sessions.value.findIndex((s) => s.id === u.id)
      if (idx >= 0) sessions.value[idx] = u
    }
    selected.value = []
    ElMessage.success(`已批准 ${updated.length} 个会话`)
  } catch {
    // 全局拦截器处理
  } finally {
    batchApproving.value = false
  }
}

async function handleBatchDisable() {
  if (selected.value.length === 0) return
  const reason = await promptReason(`批量禁用 ${selected.value.length} 个会话`, '确认禁用')
  if (reason === null) return
  batchDisabling.value = true
  try {
    const { updated } = await batchDisableSession(selected.value.map((s) => s.id), reason || undefined)
    for (const u of updated) {
      const idx = sessions.value.findIndex((s) => s.id === u.id)
      if (idx >= 0) sessions.value[idx] = u
    }
    selected.value = []
    ElMessage.success(`已禁用 ${updated.length} 个会话`)
  } catch {
    // 全局拦截器处理
  } finally {
    batchDisabling.value = false
  }
}

onMounted(async () => {
  loading.value = true
  try {
    await fetchSessions()
  } finally {
    loading.value = false
  }
  eventBus.on('session:created', onSessionEvent)
  eventBus.on('session:pendingApproval', onSessionEvent)
  eventBus.on('session:approved', onSessionEvent)
  eventBus.on('session:disabled', onSessionEvent)
})

function onSessionEvent() {
  fetchSessions()
}

onUnmounted(() => {
  eventBus.off('session:created', onSessionEvent)
  eventBus.off('session:pendingApproval', onSessionEvent)
  eventBus.off('session:approved', onSessionEvent)
  eventBus.off('session:disabled', onSessionEvent)
})
</script>

<style scoped>
.approvals-page {
  padding: 24px;
  height: 100%;
  overflow-y: auto;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
  flex-wrap: wrap;
  gap: 12px;
}

.page-title {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.loading-wrap {
  padding: 20px;
}

.approvals-table {
  width: 100%;
}

.session-title {
  font-weight: 500;
}

.reason-text {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.reason-empty {
  color: var(--el-text-color-placeholder);
}
</style>
