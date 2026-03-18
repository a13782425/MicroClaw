<template>
  <div class="approvals-page">
    <div class="page-header">
      <h2 class="page-title">会话审批</h2>
      <el-button :icon="Refresh" circle size="small" @click="refresh" :loading="refreshing" title="刷新" />
    </div>

    <div v-if="loading" class="loading-wrap">
      <el-skeleton :rows="6" animated />
    </div>

    <div v-else-if="sessions.length === 0" class="empty-wrap">
      <el-empty description="暂无会话" />
    </div>

    <div v-else class="card-grid">
      <el-card
        v-for="session in sessions"
        :key="session.id"
        shadow="hover"
        class="approval-card"
        :class="{ approved: session.isApproved }"
      >
        <div class="card-top">
          <span class="card-title">{{ session.title }}</span>
          <el-tag
            :type="session.isApproved ? 'success' : 'warning'"
            effect="plain"
            size="small"
          >{{ session.isApproved ? '已批准' : '待审批' }}</el-tag>
        </div>

        <div class="card-meta">
          <div class="meta-item">
            <el-icon><Connection /></el-icon>
            <span>{{ channelLabel(session.channelType) }}</span>
          </div>
          <div class="meta-item">
            <el-icon><Clock /></el-icon>
            <span>{{ formatTime(session.createdAt) }}</span>
          </div>
          <div class="meta-item">
            <el-icon><Cpu /></el-icon>
            <span>{{ providerName(session.providerId) }}</span>
          </div>
        </div>

        <div class="card-actions">
          <el-button
            v-if="!session.isApproved"
            type="success"
            :icon="Check"
            size="small"
            @click="handleApprove(session.id)"
          >批准</el-button>
          <el-button
            v-else
            type="warning"
            :icon="CircleClose"
            size="small"
            @click="handleDisable(session.id)"
          >禁用</el-button>
        </div>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  listSessions,
  listProviders,
  approveSession,
  disableSession,
  type SessionInfo,
  type ProviderConfig,
} from '@/services/gatewayApi'
import {
  Check, CircleClose, Connection, Clock, Cpu, Refresh,
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const sessions = ref<SessionInfo[]>([])
const providers = ref<ProviderConfig[]>([])
const loading = ref(false)
const refreshing = ref(false)

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

async function handleApprove(id: string) {
  const updated = await approveSession(id)
  const idx = sessions.value.findIndex((s) => s.id === id)
  if (idx >= 0) sessions.value[idx] = updated
  ElMessage.success('会话已批准')
}

async function handleDisable(id: string) {
  await ElMessageBox.confirm('禁用后该会话将无法继续对话，确定禁用？', '禁用确认', {
    type: 'warning',
    confirmButtonText: '禁用',
    cancelButtonText: '取消',
  })
  const updated = await disableSession(id)
  const idx = sessions.value.findIndex((s) => s.id === id)
  if (idx >= 0) sessions.value[idx] = updated
  ElMessage.success('会话已禁用')
}

onMounted(async () => {
  loading.value = true
  try {
    await fetchSessions()
  } finally {
    loading.value = false
  }
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
  gap: 12px;
  margin-bottom: 20px;
}

.page-title {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.loading-wrap {
  padding: 20px;
}

.empty-wrap {
  display: flex;
  justify-content: center;
  padding: 60px 0;
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.approval-card {
  border-left: 4px solid var(--el-color-warning);
  transition: border-color 0.2s;
}

.approval-card.approved {
  border-left-color: var(--el-color-success);
}

.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.card-title {
  font-size: 15px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  margin-right: 8px;
}

.card-meta {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 16px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.card-actions {
  display: flex;
  justify-content: flex-end;
}
</style>
