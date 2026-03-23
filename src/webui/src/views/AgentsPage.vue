<template>
  <div class="agents-layout">
    <!-- ── 左侧代理列表 ──────────────────────────────────── -->
    <div class="agents-sidebar">
      <div class="sidebar-header">
        <span class="sidebar-title">代理</span>
        <el-button size="small" :icon="Plus" circle title="添加代理" @click="openCreateDialog" />
      </div>

      <div v-if="loading" class="sidebar-loading">
        <el-skeleton :rows="3" animated />
      </div>

      <div v-else-if="agents.length === 0" class="sidebar-empty">
        <el-empty description="暂无代理" :image-size="60" />
      </div>

      <div v-else class="agent-list">
        <div
          v-for="a in agents"
          :key="a.id"
          class="agent-item"
          :class="{ active: selectedAgent?.id === a.id, 'is-disabled': !a.isEnabled }"
          @click="selectAgent(a)"
        >
          <div class="agent-avatar">{{ a.name[0]?.toUpperCase() }}</div>
          <div class="agent-info">
            <div class="agent-name-row">
              <span class="agent-name">{{ a.name }}</span>
              <el-tag v-if="a.isDefault" type="warning" size="small" effect="plain">DEFAULT</el-tag>
            </div>
          </div>
          <div class="agent-status-dot" :class="a.isEnabled ? 'enabled' : 'offline'" />
        </div>
      </div>
    </div>

    <!-- ── 右侧详情面板 ──────────────────────────────────── -->
    <div v-if="selectedAgent" class="agents-detail">
      <!-- Header -->
      <div class="detail-header">
        <div class="detail-icon">{{ selectedAgent.name[0]?.toUpperCase() }}</div>
        <div class="detail-title-block">
          <div class="detail-name-row">
            <h2 class="detail-name">{{ selectedAgent.name }}</h2>
            <el-tag v-if="selectedAgent.isDefault" type="warning" effect="plain">DEFAULT</el-tag>
          </div>
        </div>
        <div class="detail-actions">
          <el-switch
            v-model="selectedAgent.isEnabled"
            active-text="启用"
            inactive-text="停用"
            @change="(val: boolean) => onToggleEnabled(selectedAgent!, val)"
          />
          <el-button :icon="Edit" @click="openEditDialog(selectedAgent)">编辑</el-button>
          <el-button
            :icon="Delete"
            type="danger"
            plain
            :disabled="selectedAgent.isDefault"
            :title="selectedAgent.isDefault ? '默认代理不可删除' : '删除代理'"
            @click="confirmDelete(selectedAgent)"
          >删除</el-button>
        </div>
      </div>

      <!-- Tabs -->
      <el-tabs v-model="activeTab" class="detail-tabs">
        <!-- 概览 -->
        <el-tab-pane label="概览" name="overview">
          <div class="overview-grid">
            <div class="overview-card">
              <div class="ov-label">MCP Server</div>
              <div class="ov-value">{{ selectedAgent.enabledMcpServerIds.length }} 个</div>
            </div>
            <div class="overview-card">
              <div class="ov-label">绑定技能</div>
              <div class="ov-value">{{ selectedAgent.boundSkillIds.length }} 个</div>
            </div>
            <div class="overview-card">
              <div class="ov-label">状态</div>
              <div class="ov-value">
                <el-tag :type="selectedAgent.isEnabled ? 'success' : 'info'">
                  {{ selectedAgent.isEnabled ? '启用' : '停用' }}
                </el-tag>
              </div>
            </div>
          </div>
          <div class="ov-prompt-block">
            <div class="ov-label">描述</div>
            <pre v-if="selectedAgent.description" class="ov-prompt">{{ selectedAgent.description }}</pre>
            <span v-else class="ov-empty-text">（未设置）</span>
          </div>
        </el-tab-pane>



        <!-- 工具 -->
        <el-tab-pane label="工具" name="tools">
          <div class="tools-section">
            <div class="tools-section-header">
              <span>工具分组</span>
              <div style="display:flex;gap:8px;align-items:center">
                <el-button
                  v-if="toolSettingsDirty"
                  type="primary"
                  size="small"
                  :loading="savingToolSettings"
                  @click="saveToolSettings"
                >保存工具设置</el-button>
                <el-button size="small" :loading="toolsLoading" @click="loadTools(selectedAgent!)">刷新</el-button>
              </div>
            </div>
            <div v-if="toolsLoading" class="tab-loading"><el-skeleton :rows="3" animated /></div>
            <el-empty
              v-else-if="toolGroups.length === 0"
              description="点击「刷新」加载工具列表"
              :image-size="60"
            />
            <el-collapse v-else>
              <el-collapse-item v-for="group in toolGroups" :key="group.id" :name="group.id">
                <template #title>
                  <div class="tool-group-header">
                    <el-switch
                      :model-value="group.isEnabled"
                      size="small"
                      style="margin-right:8px"
                      @change="(v: boolean) => { group.isEnabled = v; onGroupToggle() }"
                      @click.stop
                    />
                    <span class="tool-group-name">{{ group.name }}</span>
                    <el-tag :type="group.type === 'builtin' ? 'warning' : 'info'" size="small" style="margin-left:8px">
                      {{ group.type === 'builtin' ? '内置' : 'MCP' }}
                    </el-tag>
                    <span class="tool-group-count">{{ group.tools.length }} 个工具</span>
                  </div>
                </template>
                <div class="tool-list">
                  <div v-for="tool in group.tools" :key="tool.name" class="tool-item">
                    <el-switch
                      :model-value="tool.isEnabled"
                      size="small"
                      :disabled="!group.isEnabled"
                      @change="(v: boolean) => { tool.isEnabled = v; onToolToggle(group, tool) }"
                    />
                    <div class="tool-info">
                      <span class="tool-name">{{ tool.name }}</span>
                      <span class="tool-desc">{{ tool.description }}</span>
                    </div>
                  </div>
                </div>
              </el-collapse-item>
            </el-collapse>
          </div>
        </el-tab-pane>

        <!-- MCP -->
        <el-tab-pane label="MCP" name="mcp">
          <div class="tools-section">
            <div class="tools-section-header">
              <span>MCP Server（全局引用）</span>
              <el-tag type="info" size="small">{{ selectedAgent.enabledMcpServerIds.length }} 个已启用</el-tag>
            </div>
            <div v-if="mcpServersLoading" class="tab-loading"><el-skeleton :rows="2" animated /></div>
            <el-empty v-else-if="allMcpServers.length === 0" description="暂无全局 MCP Server，请先在 MCP 管理页创建" :image-size="60" />
            <div v-else class="mcp-server-list">
              <div v-for="srv in allMcpServers" :key="srv.id" class="mcp-srv-row">
                <el-switch
                  :model-value="mcpEnabledIds.includes(srv.id)"
                  size="small"
                  @change="(v: boolean) => toggleMcpServer(srv.id, v)"
                />
                <el-tag size="small" type="info">{{ srv.transportType }}</el-tag>
                <span class="mcp-srv-name">{{ srv.name }}</span>
                <span class="mcp-srv-detail">
                  {{ srv.transportType === 'stdio' ? [srv.command, ...(srv.args ?? [])].join(' ') : srv.url }}
                </span>
                <el-tag v-if="!srv.isEnabled" type="warning" size="small">全局已禁用</el-tag>
              </div>
            </div>
            <div v-if="mcpSelectionDirty" style="margin-top:12px">
              <el-button type="primary" size="small" :loading="savingMcpSelection" @click="saveMcpSelection">保存 MCP 引用</el-button>
            </div>
          </div>
        </el-tab-pane>

        <!-- 技能 -->
        <el-tab-pane label="技能" name="skills">
          <div class="tab-toolbar">
            <el-button size="small" :loading="skillsLoading" @click="loadAgentSkills(selectedAgent!)">刷新</el-button>
            <el-button
              v-if="skillsDirty"
              type="primary"
              size="small"
              :loading="savingSkills"
              style="margin-left:8px"
              @click="saveAgentSkills"
            >保存技能绑定</el-button>
          </div>
          <div v-if="skillsLoading" class="tab-loading"><el-skeleton :rows="3" animated /></div>
          <el-empty v-else-if="allSkills.length === 0" description="暂无可用技能" :image-size="60" />
          <div v-else class="skills-binding-list">
            <div v-for="sk in allSkills" :key="sk.id" class="skill-bind-item">
              <el-checkbox
                :model-value="boundSkillIds.includes(sk.id)"
                @change="(v: boolean) => toggleSkill(sk.id, v)"
              />
              <div class="skill-bind-info">
                <span class="skill-bind-name">{{ sk.name }}</span>
                <el-tag size="small" effect="plain">{{ sk.skillType }}</el-tag>
                <span class="skill-bind-desc">{{ sk.description }}</span>
              </div>
            </div>
          </div>
        </el-tab-pane>

        <!-- 渠道（已移除，所有渠道消息默认路由到主 Agent） -->
      </el-tabs>
    </div>

    <!-- 未选中代理时占位 -->
    <div v-else class="agents-detail agents-detail--empty">
      <el-empty description="从左侧选择一个代理" :image-size="80" />
    </div>

    <!-- ── 创建/编辑 Dialog ──────────────────────────────── -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingAgent ? '编辑代理' : '添加代理'"
      width="680px"
      :close-on-click-modal="false"
    >
      <el-form :model="form" label-width="100px" label-position="left">
        <el-form-item label="名称" required>
          <el-input
            v-model="form.name"
            placeholder="代理名称"
            :disabled="!!editingAgent?.isDefault"
          />
          <div v-if="editingAgent?.isDefault" class="form-tip">默认代理名称不可修改</div>
        </el-form-item>
        <el-form-item label="描述">
          <el-input
            v-model="form.description"
            type="textarea"
            :rows="3"
            placeholder="代理功能描述（可选）"
          />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveAgent">保存</el-button>
      </template>
    </el-dialog>

  </div>
</template>







<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Edit, Delete } from '@element-plus/icons-vue'
import {
  listAgents, createAgent, updateAgent, deleteAgent,
  listAgentTools, updateAgentToolSettings,
  listSkills, updateAgentSkills,
  listMcpServers,
  type AgentConfig, type ToolGroup, type ToolGroupConfig, type McpServerConfig, type SkillConfig,
} from '@/services/gatewayApi'

// ── 列表状态 ──────────────────────────────────────────────────────────────────
const loading = ref(false)
const agents = ref<AgentConfig[]>([])

async function loadData() {
  loading.value = true
  try {
    agents.value = await listAgents()
  } finally {
    loading.value = false
  }
}

onMounted(loadData)

function truncate(str: string, len: number) {
  return str.length > len ? str.slice(0, len) + '…' : str
}

// ── 选中代理 ──────────────────────────────────────────────────────────────────
const selectedAgent = ref<AgentConfig | null>(null)
const activeTab = ref('overview')

function selectAgent(a: AgentConfig) {
  if (selectedAgent.value?.id !== a.id) {
    selectedAgent.value = a
    activeTab.value = 'overview'
    // 重置工具和文件列表
    toolGroups.value = []
    toolSettingsDirty.value = false
    allSkills.value = []
    boundSkillIds.value = []
    skillsDirty.value = false
    mcpEnabledIds.value = [...a.enabledMcpServerIds]
    mcpSelectionDirty.value = false
  }
}

// 切换 Tab 时懒加载数据
watch(activeTab, async (tab) => {
  if (!selectedAgent.value) return
  if (tab === 'tools') {
    await loadTools(selectedAgent.value)
  }
  if (tab === 'mcp') {
    await loadMcpServers()
  }
  if (tab === 'skills') await loadAgentSkills(selectedAgent.value)
})

// ── 启用/停用 ─────────────────────────────────────────────────────────────────
async function onToggleEnabled(a: AgentConfig, val: boolean) {
  try {
    await updateAgent({ id: a.id, isEnabled: val })
    ElMessage.success(val ? '已启用' : '已停用')
    await loadData()
    // 同步 selectedAgent
    const updated = agents.value.find(ag => ag.id === a.id)
    if (updated) selectedAgent.value = updated
  } catch {
    a.isEnabled = !val  // 回滚开关状态
  }
}

// ── 创建/编辑 Dialog ──────────────────────────────────────────────────────────
interface AgentForm {
  name: string
  description: string
  isEnabled: boolean
}

const dialogVisible = ref(false)
const saving = ref(false)
const editingAgent = ref<AgentConfig | null>(null)

const form = ref<AgentForm>({
  name: '',
  description: '',
  isEnabled: true,
})

function openCreateDialog() {
  editingAgent.value = null
  form.value = { name: '', description: '', isEnabled: true }
  dialogVisible.value = true
}

function openEditDialog(a: AgentConfig) {
  editingAgent.value = a
  form.value = {
    name: a.name,
    description: a.description,
    isEnabled: a.isEnabled,
  }
  dialogVisible.value = true
}

async function saveAgent() {
  if (!form.value.name.trim()) { ElMessage.warning('请填写 Agent 名称'); return }

  saving.value = true
  try {
    if (editingAgent.value) {
      await updateAgent({
        id: editingAgent.value.id,
        name: form.value.name,
        description: form.value.description,
        isEnabled: form.value.isEnabled,
      })
      ElMessage.success('已保存')
    } else {
      await createAgent({
        name: form.value.name,
        description: form.value.description,
        isEnabled: form.value.isEnabled,
      })
      ElMessage.success('创建成功')
    }
    dialogVisible.value = false
    await loadData()
    // 同步 selectedAgent
    if (editingAgent.value) {
      const updated = agents.value.find(ag => ag.id === editingAgent.value!.id)
      if (updated) selectedAgent.value = updated
    }
  } catch {
    // 保存失败由全局拦截器展示后端错误信息
  } finally {
    saving.value = false
  }
}

async function confirmDelete(a: AgentConfig) {
  await ElMessageBox.confirm(`确定删除 Agent「${a.name}」？`, '删除确认', {
    confirmButtonText: '删除',
    cancelButtonText: '取消',
    type: 'warning',
  })
  try {
    await deleteAgent(a.id)
    ElMessage.success('已删除')
    if (selectedAgent.value?.id === a.id) selectedAgent.value = null
    await loadData()
  } catch {
    // 删除失败由全局拦截器展示后端错误信息
  }
}

// ── 全局 MCP Server 引用管理 ──────────────────────────────────────────────────
const mcpServersLoading = ref(false)
const allMcpServers = ref<McpServerConfig[]>([])
const mcpEnabledIds = ref<string[]>([])
const mcpSelectionDirty = ref(false)
const savingMcpSelection = ref(false)

async function loadMcpServers() {
  mcpServersLoading.value = true
  try {
    allMcpServers.value = await listMcpServers()
  } catch {
    // 加载失败由全局拦截器展示后端错误信息
  } finally {
    mcpServersLoading.value = false
  }
}

function toggleMcpServer(id: string, enabled: boolean) {
  if (enabled) {
    if (!mcpEnabledIds.value.includes(id)) mcpEnabledIds.value = [...mcpEnabledIds.value, id]
  } else {
    mcpEnabledIds.value = mcpEnabledIds.value.filter(x => x !== id)
  }
  mcpSelectionDirty.value = true
}

async function saveMcpSelection() {
  if (!selectedAgent.value) return
  savingMcpSelection.value = true
  try {
    await updateAgent({ id: selectedAgent.value.id, enabledMcpServerIds: mcpEnabledIds.value })
    selectedAgent.value = { ...selectedAgent.value, enabledMcpServerIds: [...mcpEnabledIds.value] }
    mcpSelectionDirty.value = false
    ElMessage.success('MCP 引用已保存')
    await loadData()
  } catch {
    // 保存失败由全局拦截器展示后端错误信息
  } finally {
    savingMcpSelection.value = false
  }
}

// ── 工具分组 ──────────────────────────────────────────────────────────────────
const toolsLoading = ref(false)
const toolGroups = ref<ToolGroup[]>([])
const toolSettingsDirty = ref(false)
const savingToolSettings = ref(false)

async function loadTools(a: AgentConfig) {
  toolsLoading.value = true
  toolGroups.value = []
  toolSettingsDirty.value = false
  try {
    const result = await listAgentTools(a.id)
    toolGroups.value = result.groups
  } catch {
    // 加载失败由全局拦截器展示后端错误信息
  } finally {
    toolsLoading.value = false
  }
}

function onGroupToggle() {
  toolSettingsDirty.value = true
}

function onToolToggle(group: ToolGroup, tool: { name: string; isEnabled: boolean }) {
  // 若组内有工具被禁用，自动保持组级别不变；用户自行控制
  if (!tool.isEnabled) {
    // 确保 group 开关不影响单个工具的状态
  }
  toolSettingsDirty.value = true
}

async function saveToolSettings() {
  if (!selectedAgent.value) return
  savingToolSettings.value = true
  try {
    const configs: ToolGroupConfig[] = toolGroups.value.map(g => ({
      groupId: g.id,
      isEnabled: g.isEnabled,
      disabledToolNames: g.tools.filter(t => !t.isEnabled).map(t => t.name),
    }))
    await updateAgentToolSettings(selectedAgent.value.id, configs)
    toolSettingsDirty.value = false
    ElMessage.success('工具设置已保存')
  } catch {
    // 保存失败由全局拦截器展示后端错误信息
  } finally {
    savingToolSettings.value = false
  }
}

// ── 技能绑定 ──────────────────────────────────────────────────────────────────
const skillsLoading = ref(false)
const allSkills = ref<SkillConfig[]>([])
const boundSkillIds = ref<string[]>([])
const skillsDirty = ref(false)
const savingSkills = ref(false)

async function loadAgentSkills(a: AgentConfig) {
  skillsLoading.value = true
  try {
    allSkills.value = await listSkills()
    boundSkillIds.value = [...(a.boundSkillIds ?? [])]
    skillsDirty.value = false
  } finally {
    skillsLoading.value = false
  }
}

function toggleSkill(skillId: string, checked: boolean) {
  if (checked) {
    if (!boundSkillIds.value.includes(skillId)) boundSkillIds.value.push(skillId)
  } else {
    boundSkillIds.value = boundSkillIds.value.filter(id => id !== skillId)
  }
  skillsDirty.value = true
}

async function saveAgentSkills() {
  if (!selectedAgent.value) return
  savingSkills.value = true
  try {
    await updateAgentSkills(selectedAgent.value.id, [...boundSkillIds.value])
    skillsDirty.value = false
    ElMessage.success('技能绑定已保存')
    await loadData()
    const updated = agents.value.find(a => a.id === selectedAgent.value!.id)
    if (updated) selectedAgent.value = updated
  } catch {
    // 保存失败由全局拦截器展示后端错误信息
  } finally {
    savingSkills.value = false
  }
}
</script>

<style scoped>
/* ── Layout ────────────────────────────────────── */
.agents-layout {
  display: flex;
  height: 100%;
  overflow: hidden;
}

/* ── Sidebar ───────────────────────────────────── */
.agents-sidebar {
  width: 260px;
  flex-shrink: 0;
  border-right: 1px solid var(--el-border-color);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px;
  border-bottom: 1px solid var(--el-border-color);
}

.sidebar-title {
  font-size: 15px;
  font-weight: 600;
}

.sidebar-loading,
.sidebar-empty {
  padding: 24px;
}

.agent-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.agent-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  cursor: pointer;
  transition: background 0.15s;
}

.agent-item:hover {
  background: var(--el-fill-color-light);
}

.agent-item.active {
  background: var(--el-color-primary-light-9);
}

.agent-item.is-disabled {
  opacity: 0.6;
}

.agent-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--el-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 15px;
  font-weight: 700;
  flex-shrink: 0;
}

.agent-info {
  flex: 1;
  min-width: 0;
}

.agent-name-row {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.agent-name {
  font-size: 14px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.agent-provider {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.agent-status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.agent-status-dot.enabled { background: var(--el-color-success); }
.agent-status-dot.offline  { background: var(--el-color-info); }

/* ── Detail Panel ──────────────────────────────── */
.agents-detail {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.agents-detail--empty {
  align-items: center;
  justify-content: center;
}

.detail-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--el-border-color);
  flex-shrink: 0;
}

.detail-icon {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: var(--el-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
  font-weight: 700;
  flex-shrink: 0;
}

.detail-title-block {
  flex: 1;
  min-width: 0;
}

.detail-name-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.detail-name {
  margin: 0 0 4px;
  font-size: 18px;
  font-weight: 700;
}

.detail-subtitle {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.detail-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.detail-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0 24px;
}

.detail-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow-y: auto;
  padding-top: 16px;
}

/* ── Overview Tab ──────────────────────────────── */
.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}

.overview-card {
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  padding: 12px;
}

.ov-label {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  margin-bottom: 4px;
}

.ov-value {
  font-size: 14px;
  font-weight: 600;
}

.ov-prompt-block {
  margin-top: 12px;
}

.ov-prompt {
  margin: 8px 0 0;
  padding: 12px;
  background: var(--el-fill-color);
  border-radius: 6px;
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
  max-height: 300px;
  overflow-y: auto;
}

.ov-empty-text {
  font-size: 13px;
  color: var(--el-text-color-placeholder);
}

/* ── Files / Tools Tabs ────────────────────────── */
.tab-toolbar {
  margin-bottom: 12px;
}

.tab-loading {
  padding: 16px 0;
}

.file-preview {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.snapshot-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.snapshot-card {
  cursor: pointer;
  border: 1px solid var(--el-border-color);
  transition: border-color 0.2s;
}

.snapshot-card:hover {
  border-color: var(--el-color-primary-light-5);
}

.snapshot-selected {
  border-color: var(--el-color-primary);
}

.snapshot-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.snapshot-time {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.snapshot-preview {
  margin-top: 10px;
  font-size: 12px;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 200px;
  overflow-y: auto;
  background: var(--el-fill-color-light);
  border-radius: 4px;
  padding: 8px;
}

.tools-section {
  margin-bottom: 20px;
}

.tools-section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
  font-weight: 600;
  font-size: 13px;
}

.mcp-server-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.mcp-server-card {
  border: 1px solid var(--el-border-color) !important;
  border-radius: 6px !important;
}

.mcp-srv-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.mcp-srv-name {
  font-weight: 600;
  font-size: 13px;
}

.mcp-srv-detail {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  font-family: monospace;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
}

/* ── Tool Groups ─────────────────────────────────── */
.tool-group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}

.tool-group-name {
  font-weight: 600;
}

.tool-group-count {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-right: 12px;
}

.tool-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 4px 0;
}

.tool-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--el-fill-color-lighter);
}

.tool-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
}

.tool-name {
  font-size: 13px;
  font-weight: 600;
  font-family: monospace;
}

.tool-desc {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
}

/* ── Form styles ───────────────────────────────── */
.placeholder-icon {
  font-size: 72px;
  color: var(--el-text-color-placeholder);
}

.mcp-server-row {
  margin-bottom: 12px;
}

.mcp-card {
  border: 1px dashed var(--el-border-color) !important;
}

.mcp-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.mcp-title {
  font-weight: 600;
  font-size: 13px;
}

.form-tip {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
  margin-top: 4px;
}

/* ── Skills Binding Tab ────────────────────────── */
.skills-binding-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.skill-bind-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--el-fill-color-lighter);
}

.skill-bind-info {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  flex-wrap: wrap;
}

.skill-bind-name {
  font-size: 13px;
  font-weight: 600;
}

.skill-bind-desc {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
