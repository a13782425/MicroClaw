<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">DNA 管理</h2>
    </div>

    <el-tabs v-model="activeTab" class="dna-tabs" @tab-change="onTabChange">
      <!-- ── 全局 DNA ─────────────────────────────────────── -->
      <el-tab-pane label="全局 DNA" name="global">
        <div class="tab-desc">全局 DNA 注入所有 Agent 的 SystemPrompt 上下文（三层架构第一层）。</div>
        <DnaFilePanel scope="global" />
      </el-tab-pane>

      <!-- ── Agent DNA ───────────────────────────────────── -->
      <el-tab-pane label="Agent DNA" name="agent">
        <div class="selector-row">
          <span class="selector-label">选择 Agent：</span>
          <el-select
            v-model="selectedAgentId"
            placeholder="请选择 Agent"
            style="width: 260px"
            :loading="agentsLoading"
            @change="onAgentChange"
          >
            <el-option
              v-for="agent in agents"
              :key="agent.id"
              :label="agent.name"
              :value="agent.id"
            />
          </el-select>
        </div>
        <el-empty v-if="!selectedAgentId" description="请先选择一个 Agent" :image-size="80" />
        <DnaFilePanel v-else scope="agent" :scope-id="selectedAgentId" />
      </el-tab-pane>

      <!-- ── 会话 DNA ────────────────────────────────────── -->
      <el-tab-pane label="会话 DNA" name="session">
        <div class="selector-row">
          <span class="selector-label">选择会话：</span>
          <el-select
            v-model="selectedSessionId"
            placeholder="请选择会话"
            style="width: 320px"
            :loading="sessionsLoading"
            filterable
            @change="onSessionChange"
          >
            <el-option
              v-for="session in sessions"
              :key="session.id"
              :label="session.title"
              :value="session.id"
            />
          </el-select>
        </div>
        <el-empty v-if="!selectedSessionId" description="请先选择一个会话" :image-size="80" />
        <DnaFilePanel v-else scope="session" :scope-id="selectedSessionId" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import DnaFilePanel from '@/components/DnaFilePanel.vue'
import { listAgents, listSessions, type AgentConfig, type SessionInfo } from '@/services/gatewayApi'

// ── 路由参数（支持 /dna?tab=agent&id=xxx 直接跳入） ──────────────────────────

const route = useRoute()
const router = useRouter()

const activeTab = ref<'global' | 'agent' | 'session'>('global')
const selectedAgentId = ref<string>('')
const selectedSessionId = ref<string>('')

// ── 数据加载 ──────────────────────────────────────────────────────────────────

const agentsLoading = ref(false)
const agents = ref<AgentConfig[]>([])

const sessionsLoading = ref(false)
const sessions = ref<SessionInfo[]>([])

async function loadAgents() {
  agentsLoading.value = true
  try { agents.value = await listAgents() } finally { agentsLoading.value = false }
}

async function loadSessions() {
  sessionsLoading.value = true
  try { sessions.value = await listSessions() } finally { sessionsLoading.value = false }
}

// ── 初始化（从路由参数恢复状态） ────────────────────────────────────────────────

onMounted(async () => {
  await Promise.all([loadAgents(), loadSessions()])

  const tab = route.query.tab as string | undefined
  const id = route.query.id as string | undefined

  if (tab === 'agent') {
    activeTab.value = 'agent'
    if (id) selectedAgentId.value = id
  } else if (tab === 'session') {
    activeTab.value = 'session'
    if (id) selectedSessionId.value = id
  }
})

// ── 页签切换同步 URL ─────────────────────────────────────────────────────────

function onTabChange(tab: string) {
  const query: Record<string, string> = { tab }
  if (tab === 'agent' && selectedAgentId.value) query.id = selectedAgentId.value
  if (tab === 'session' && selectedSessionId.value) query.id = selectedSessionId.value
  router.replace({ path: '/dna', query })
}

function onAgentChange(id: string) {
  router.replace({ path: '/dna', query: { tab: 'agent', id } })
}

function onSessionChange(id: string) {
  router.replace({ path: '/dna', query: { tab: 'session', id } })
}
</script>

<style scoped>
.page-container {
  padding: 24px;
  max-width: 1200px;
}

.page-header {
  margin-bottom: 16px;
}

.page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}

.dna-tabs {
  background: #fff;
  border-radius: 8px;
  padding: 16px;
  box-shadow: var(--el-box-shadow-light);
}

.tab-desc {
  color: var(--el-text-color-secondary);
  font-size: 13px;
  margin-bottom: 16px;
}

.selector-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
}

.selector-label {
  font-size: 14px;
  color: var(--el-text-color-regular);
  white-space: nowrap;
}
</style>
