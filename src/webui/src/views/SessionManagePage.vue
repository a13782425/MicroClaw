<template>
  <div class="session-manage-layout">
    <!-- 左侧：会话列表 -->
    <div class="sessions-sidebar">
      <div class="sidebar-header">
        <span class="sidebar-title">会话管理</span>
        <el-button :icon="Refresh" circle size="small" :loading="listLoading" title="刷新" @click="loadSessions" />
      </div>

      <div v-if="listLoading" class="sidebar-loading">
        <el-skeleton :rows="4" animated />
      </div>

      <div v-else-if="sessions.length === 0" class="sidebar-empty">
        <el-empty description="暂无会话" :image-size="60" />
      </div>

      <div v-else class="session-list">
        <div
          v-for="s in sessions"
          :key="s.id"
          class="session-item"
          :class="{ active: selectedSession?.id === s.id }"
          @click="selectSession(s)"
        >
          <div class="session-name">{{ s.title }}</div>
          <div class="session-meta">
            <el-tag
              :type="s.isApproved ? 'success' : 'warning'"
              size="small"
              effect="plain"
            >{{ s.isApproved ? '已批准' : '待审批' }}</el-tag>
            <span class="session-time">{{ formatTime(s.createdAt) }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 右侧：详情区 -->
    <div class="session-detail">
      <div v-if="!selectedSession" class="detail-empty">
        <el-empty description="从左侧选择一个会话" :image-size="80" />
      </div>

      <template v-else>
        <!-- 会话标题 -->
        <div class="detail-header">
          <span class="detail-title">{{ selectedSession.title }}</span>
          <el-tag
            :type="selectedSession.isApproved ? 'success' : 'warning'"
            effect="plain"
          >{{ selectedSession.isApproved ? '已批准' : '待审批' }}</el-tag>
        </div>

        <!-- 功能 Tabs -->
        <el-tabs v-model="activeTab" class="detail-tabs">
          <!-- DNA Tab -->
          <el-tab-pane label="🧬 DNA" name="dna">
            <div v-if="dnaLoading" class="tab-loading"><el-skeleton :rows="6" animated /></div>
            <el-tabs v-else v-model="activeDnaFile" tab-position="top" class="dna-tabs">
              <el-tab-pane
                v-for="file in dnaFiles"
                :key="file.fileName"
                :label="file.fileName.replace('.md', '')"
                :name="file.fileName"
              >
                <div class="dna-content">
                  <p class="dna-desc">{{ file.description }}</p>
                  <el-input
                    v-model="dnaEdits[file.fileName]"
                    type="textarea"
                    :autosize="{ minRows: 16 }"
                    resize="vertical"
                    spellcheck="false"
                    class="dna-editor"
                  />
                  <div class="action-row">
                    <el-button
                      type="primary"
                      :loading="dnaSaving"
                      @click="saveDna(file.fileName)"
                    >保存</el-button>
                  </div>
                </div>
              </el-tab-pane>
            </el-tabs>
          </el-tab-pane>

          <!-- 记忆 Tab -->
          <el-tab-pane label="🧠 记忆" name="memory">
            <!-- 长期记忆 -->
            <div class="memory-section">
              <div class="section-header">
                <span class="section-title">📒 长期记忆 (MEMORY.md)</span>
                <el-button
                  type="primary"
                  size="small"
                  :loading="memorySaving"
                  @click="saveMemory"
                >保存</el-button>
              </div>
              <p class="section-desc">记录用户长期偏好、重要信息和历史摘要，始终注入到 System Prompt。</p>
              <div v-if="memoryLoading" class="tab-loading"><el-skeleton :rows="5" animated /></div>
              <el-input
                v-else
                v-model="memoryContent"
                type="textarea"
                :autosize="{ minRows: 8 }"
                resize="vertical"
                spellcheck="false"
                placeholder="此 Session 的长期记忆（Markdown 格式）..."
                class="memory-editor"
              />
            </div>

            <!-- 每日记忆 -->
            <div class="memory-section">
              <div class="section-header">
                <span class="section-title">📅 每日记忆</span>
                <el-tag type="info" size="small">{{ dailyDates.length }} 条</el-tag>
              </div>
              <p class="section-desc">AI 自动生成的每日对话摘要，近 7 天全量注入，7-30 天仅首行摘要。</p>
              <div v-if="dailyLoading" class="tab-loading"><el-skeleton :rows="3" animated /></div>
              <div v-else-if="dailyDates.length === 0" class="list-empty">
                <el-empty description="暂无每日记忆（由 AI 自动生成）" :image-size="60" />
              </div>
              <div v-else class="daily-list">
                <div
                  v-for="date in dailyDates"
                  :key="date"
                  class="daily-item"
                >
                  <div class="daily-header" @click="toggleDaily(date)">
                    <el-icon class="expand-icon" :class="{ expanded: expandedDates.has(date) }">
                      <ArrowRight />
                    </el-icon>
                    <span class="daily-date">{{ date }}</span>
                    <el-tag v-if="getDaysAgo(date) <= 7" size="small" type="success">近 7 天</el-tag>
                    <el-tag v-else-if="getDaysAgo(date) <= 30" size="small" type="warning">7-30 天</el-tag>
                    <el-tag v-else size="small" type="info">30+ 天</el-tag>
                  </div>
                  <div v-if="expandedDates.has(date)" class="daily-body">
                    <el-skeleton v-if="!dailyContents[date]" :rows="2" animated />
                    <pre v-else class="daily-text">{{ dailyContents[date] }}</pre>
                  </div>
                </div>
              </div>
            </div>
          </el-tab-pane>

          <!-- 审批 Tab -->
          <el-tab-pane label="✅ 审批" name="approval">
            <div class="approval-panel">
              <div class="approval-status">
                <span class="status-label">当前状态：</span>
                <el-tag
                  :type="selectedSession.isApproved ? 'success' : 'warning'"
                  size="large"
                  effect="plain"
                >{{ selectedSession.isApproved ? '已批准' : '待审批' }}</el-tag>
              </div>

              <div v-if="selectedSession.approvalReason" class="approval-reason">
                <span class="reason-label">审批原因：</span>
                <span class="reason-text">{{ selectedSession.approvalReason }}</span>
              </div>

              <div class="approval-actions">
                <el-button
                  v-if="!selectedSession.isApproved"
                  type="success"
                  :icon="Check"
                  :loading="approving"
                  @click="handleApprove"
                >批准此会话</el-button>
                <el-button
                  v-else
                  type="warning"
                  :icon="CircleClose"
                  :loading="approving"
                  @click="handleDisable"
                >禁用此会话</el-button>
              </div>
            </div>
          </el-tab-pane>
        </el-tabs>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  listSessions, approveSession, disableSession,
  listSessionDna, updateSessionDna,
  getSessionMemory, updateSessionMemory,
  listSessionDailyMemories, getSessionDailyMemory,
  type SessionInfo, type SessionDnaFileInfo,
} from '@/services/gatewayApi'
import { Refresh, Check, CircleClose, ArrowRight } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'

// ── 会话列表 ───────────────────────────────────────────────────────────────────
const sessions = ref<SessionInfo[]>([])
const listLoading = ref(false)
const selectedSession = ref<SessionInfo | null>(null)

async function loadSessions() {
  listLoading.value = true
  try {
    sessions.value = await listSessions()
  } finally {
    listLoading.value = false
  }
}

function selectSession(s: SessionInfo) {
  if (selectedSession.value?.id === s.id) return
  selectedSession.value = s
  activeTab.value = 'dna'
  resetPanels()
}

function resetPanels() {
  dnaFiles.value = []
  dnaEdits.value = {}
  memoryContent.value = ''
  dailyDates.value = []
  expandedDates.value = new Set()
  dailyContents.value = {}
}

function formatTime(iso: string): string {
  if (!iso) return ''
  return new Date(iso).toLocaleString('zh-CN', {
    month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
  })
}

// ── Tab 切换懒加载 ─────────────────────────────────────────────────────────────
const activeTab = ref('dna')

watch(activeTab, (tab) => {
  if (!selectedSession.value) return
  if (tab === 'dna' && dnaFiles.value.length === 0) loadDnaFiles()
  if (tab === 'memory') loadMemory()
})

watch(selectedSession, (s) => {
  if (s && activeTab.value === 'dna') loadDnaFiles()
})

// ── DNA Tab ────────────────────────────────────────────────────────────────────
const dnaLoading = ref(false)
const dnaFiles = ref<SessionDnaFileInfo[]>([])
const dnaEdits = ref<Record<string, string>>({})
const dnaSaving = ref(false)
const activeDnaFile = ref('SOUL.md')

async function loadDnaFiles() {
  const sessionId = selectedSession.value?.id
  if (!sessionId) return
  dnaLoading.value = true
  try {
    const files = await listSessionDna(sessionId)
    dnaFiles.value = files
    dnaEdits.value = Object.fromEntries(files.map((f) => [f.fileName, f.content]))
    if (files.length > 0) activeDnaFile.value = files[0].fileName
  } catch {
    ElMessage.error('加载 DNA 文件失败')
  } finally {
    dnaLoading.value = false
  }
}

async function saveDna(fileName: string) {
  const sessionId = selectedSession.value?.id
  if (!sessionId) return
  dnaSaving.value = true
  try {
    const updated = await updateSessionDna(sessionId, fileName, dnaEdits.value[fileName] ?? '')
    const idx = dnaFiles.value.findIndex((f) => f.fileName === fileName)
    if (idx >= 0) dnaFiles.value[idx] = updated
    ElMessage.success(`${fileName} 已保存`)
  } catch {
    ElMessage.error('保存失败')
  } finally {
    dnaSaving.value = false
  }
}

// ── 记忆 Tab ───────────────────────────────────────────────────────────────────
const memoryLoading = ref(false)
const memoryContent = ref('')
const memorySaving = ref(false)
const dailyLoading = ref(false)
const dailyDates = ref<string[]>([])
const expandedDates = ref<Set<string>>(new Set())
const dailyContents = ref<Record<string, string>>({})

async function loadMemory() {
  const sessionId = selectedSession.value?.id
  if (!sessionId) return

  memoryLoading.value = true
  try {
    memoryContent.value = await getSessionMemory(sessionId)
  } catch {
    ElMessage.error('加载长期记忆失败')
  } finally {
    memoryLoading.value = false
  }

  dailyLoading.value = true
  try {
    dailyDates.value = await listSessionDailyMemories(sessionId)
  } catch {
    ElMessage.error('加载每日记忆列表失败')
  } finally {
    dailyLoading.value = false
  }
}

async function saveMemory() {
  const sessionId = selectedSession.value?.id
  if (!sessionId) return
  memorySaving.value = true
  try {
    memoryContent.value = await updateSessionMemory(sessionId, memoryContent.value)
    ElMessage.success('长期记忆已保存')
  } catch {
    ElMessage.error('保存失败')
  } finally {
    memorySaving.value = false
  }
}

async function toggleDaily(date: string) {
  const next = new Set(expandedDates.value)
  if (next.has(date)) {
    next.delete(date)
    expandedDates.value = next
    return
  }
  next.add(date)
  expandedDates.value = next

  if (!dailyContents.value[date] && selectedSession.value) {
    try {
      const info = await getSessionDailyMemory(selectedSession.value.id, date)
      dailyContents.value = { ...dailyContents.value, [date]: info.content }
    } catch {
      dailyContents.value = { ...dailyContents.value, [date]: '加载失败' }
    }
  }
}

function getDaysAgo(date: string): number {
  return Math.floor((Date.now() - new Date(date).getTime()) / (1000 * 60 * 60 * 24))
}

// ── 审批 Tab ───────────────────────────────────────────────────────────────────
const approving = ref(false)

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

async function handleApprove() {
  const s = selectedSession.value
  if (!s) return
  const reason = await promptReason(`批准会话「${s.title}」`, '确认批准')
  if (reason === null) return
  approving.value = true
  try {
    const updated = await approveSession(s.id, reason || undefined)
    selectedSession.value = updated
    const idx = sessions.value.findIndex((x) => x.id === s.id)
    if (idx >= 0) sessions.value[idx] = updated
    ElMessage.success('会话已批准')
  } finally {
    approving.value = false
  }
}

async function handleDisable() {
  const s = selectedSession.value
  if (!s) return
  const reason = await promptReason(`禁用会话「${s.title}」`, '确认禁用')
  if (reason === null) return
  approving.value = true
  try {
    const updated = await disableSession(s.id, reason || undefined)
    selectedSession.value = updated
    const idx = sessions.value.findIndex((x) => x.id === s.id)
    if (idx >= 0) sessions.value[idx] = updated
    ElMessage.success('会话已禁用')
  } finally {
    approving.value = false
  }
}

// ── Init ───────────────────────────────────────────────────────────────────────
loadSessions()
</script>

<style scoped>
.session-manage-layout {
  display: flex;
  height: 100%;
  overflow: hidden;
  background: var(--el-bg-color);
}

/* ── 左侧边栏 ── */
.sessions-sidebar {
  width: 260px;
  flex-shrink: 0;
  border-right: 1px solid var(--el-border-color-light);
  display: flex;
  flex-direction: column;
  background: var(--el-fill-color-blank);
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.sidebar-title {
  font-weight: 600;
  font-size: 15px;
}

.sidebar-loading,
.sidebar-empty {
  padding: 16px;
}

.session-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.session-item {
  padding: 10px 14px;
  cursor: pointer;
  border-radius: 6px;
  margin: 0 6px 2px;
  transition: background 0.15s;
}

.session-item:hover {
  background: var(--el-fill-color);
}

.session-item.active {
  background: var(--el-color-primary-light-9);
}

.session-name {
  font-size: 13px;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin-bottom: 4px;
}

.session-meta {
  display: flex;
  align-items: center;
  gap: 8px;
}

.session-time {
  font-size: 11px;
  color: var(--el-text-color-secondary);
}

/* ── 右侧详情 ── */
.session-detail {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.detail-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.detail-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 20px;
  border-bottom: 1px solid var(--el-border-color-light);
  flex-shrink: 0;
}

.detail-title {
  font-weight: 600;
  font-size: 16px;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0 20px;
}

.detail-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow-y: auto;
  padding-top: 12px;
}

/* ── DNA Tab ── */
.tab-loading {
  padding: 12px 0;
}

.dna-tabs {
  height: 100%;
}

.dna-content {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 10px;
}

.dna-desc {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  padding: 6px 10px;
  background: var(--el-fill-color-lighter);
  border-radius: 4px;
}

.dna-editor {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 13px;
}

.action-row {
  display: flex;
  justify-content: flex-end;
  padding-bottom: 8px;
}

/* ── 记忆 Tab ── */
.memory-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 24px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.section-title {
  font-size: 14px;
  font-weight: 600;
}

.section-desc {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  padding: 6px 10px;
  background: var(--el-fill-color-lighter);
  border-radius: 4px;
}

.memory-editor {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 13px;
}

.list-empty {
  padding: 12px 0;
}

.daily-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.daily-item {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  overflow: hidden;
}

.daily-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  cursor: pointer;
  user-select: none;
  background: var(--el-fill-color-lighter);
  transition: background 0.15s;
}

.daily-header:hover {
  background: var(--el-fill-color-light);
}

.expand-icon {
  transition: transform 0.2s;
  color: var(--el-text-color-secondary);
}

.expand-icon.expanded {
  transform: rotate(90deg);
}

.daily-date {
  flex: 1;
  font-size: 13px;
  font-weight: 500;
  font-family: monospace;
}

.daily-body {
  padding: 10px 12px;
  background: var(--el-bg-color);
}

.daily-text {
  margin: 0;
  font-size: 13px;
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  white-space: pre-wrap;
  word-break: break-word;
}

/* ── 审批 Tab ── */
.approval-panel {
  padding: 20px 0;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.approval-status {
  display: flex;
  align-items: center;
  gap: 12px;
}

.status-label,
.reason-label {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-primary);
}

.approval-reason {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}

.reason-text {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.approval-actions {
  display: flex;
  gap: 12px;
}
</style>
