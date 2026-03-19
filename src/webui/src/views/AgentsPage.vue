<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2 class="page-title">Agent</h2>
        <p class="page-desc">管理 AI Agent，配置 DNA 记忆与 MCP 工具（支持 Python/Node.js MCP Server）</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="openCreateDialog">添加 Agent</el-button>
    </div>

    <div v-if="loading" class="loading-wrap">
      <el-skeleton :rows="3" animated />
    </div>

    <el-empty v-else-if="agents.length === 0" description="暂无 Agent，点击右上角添加" :image-size="100">
      <template #image>
        <el-icon class="placeholder-icon"><Promotion /></el-icon>
      </template>
    </el-empty>

    <div v-else class="agent-grid">
      <div v-for="a in agents" :key="a.id" class="agent-card" :class="{ disabled: !a.isEnabled }">
        <div class="card-top">
          <div class="card-title-row">
            <span class="card-name">{{ a.name }}</span>
            <el-tag :type="a.isEnabled ? 'success' : 'info'" size="small">
              {{ a.isEnabled ? '启用' : '停用' }}
            </el-tag>
          </div>
          <div class="card-provider">
            <el-icon><Cpu /></el-icon>
            {{ providerName(a.providerId) || a.providerId }}
          </div>
          <div class="card-meta">
            <el-tag type="info" size="small">{{ a.mcpServers.length }} 个 MCP Server</el-tag>
            <el-tag type="info" size="small" class="ml-1">绑定 {{ a.boundChannelIds.length }} 渠道</el-tag>
          </div>
          <p v-if="a.systemPrompt" class="card-prompt">{{ truncate(a.systemPrompt, 80) }}</p>
        </div>

        <div class="card-bottom">
          <el-switch
            v-model="a.isEnabled"
            size="small"
            active-text="启用"
            inactive-text="停用"
            @change="(val: boolean) => toggleEnabled(a, val)"
          />
          <div class="card-actions">
            <el-button link type="primary" :icon="Edit" @click="openEditDialog(a)">编辑</el-button>
            <el-divider direction="vertical" />
            <el-button link type="info" :icon="Document" @click="openDnaDialog(a)">DNA</el-button>
            <el-divider direction="vertical" />
            <el-button link type="warning" :icon="Tools" @click="previewTools(a)">工具</el-button>
            <el-divider direction="vertical" />
            <el-button link type="danger" :icon="Delete" @click="confirmDelete(a)">删除</el-button>
          </div>
        </div>
      </div>
    </div>

    <!-- ── 创建/编辑 Dialog ──────────────────────────── -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingAgent ? '编辑 Agent' : '添加 Agent'"
      width="680px"
      :close-on-click-modal="false"
    >
      <el-form :model="form" label-width="100px" label-position="left">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="Agent 名称" />
        </el-form-item>
        <el-form-item label="模型提供方" required>
          <el-select v-model="form.providerId" placeholder="选择模型" style="width:100%">
            <el-option
              v-for="p in providers"
              :key="p.id"
              :label="p.displayName"
              :value="p.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="绑定渠道">
          <el-select
            v-model="form.boundChannelIds"
            multiple
            placeholder="选择渠道（可多选）"
            style="width:100%"
          >
            <el-option
              v-for="c in channels"
              :key="c.id"
              :label="c.displayName"
              :value="c.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="系统提示词">
          <el-input
            v-model="form.systemPrompt"
            type="textarea"
            :rows="4"
            placeholder="系统级提示词，定义 Agent 角色与行为"
          />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>

        <!-- MCP Server 配置 -->
        <el-divider>MCP Server（工具来源）</el-divider>
        <div v-for="(srv, idx) in form.mcpServers" :key="idx" class="mcp-server-row">
          <el-card shadow="never" class="mcp-card">
            <div class="mcp-header">
              <span class="mcp-title">Server {{ idx + 1 }}</span>
              <el-button link type="danger" :icon="Delete" @click="removeMcpServer(idx)" />
            </div>
            <el-form-item label="名称">
              <el-input v-model="srv.name" placeholder="如 filesystem" />
            </el-form-item>
            <el-form-item label="传输方式">
              <el-radio-group v-model="srv.transportType">
                <el-radio value="stdio">Stdio（本地进程）</el-radio>
                <el-radio value="sse">SSE（远程 HTTP）</el-radio>
              </el-radio-group>
            </el-form-item>
            <template v-if="srv.transportType === 'stdio'">
              <el-form-item label="命令">
                <el-input v-model="srv.command" placeholder="如 npx 或 uvx" />
              </el-form-item>
              <el-form-item label="参数">
                <el-input v-model="srv.argsText" placeholder="如 -y @modelcontextprotocol/server-filesystem /tmp" />
                <div class="form-tip">空格分隔，每个参数独立一项</div>
              </el-form-item>
            </template>
            <template v-else>
              <el-form-item label="URL">
                <el-input v-model="srv.url" placeholder="如 http://localhost:3000/sse" />
              </el-form-item>
            </template>
          </el-card>
        </div>
        <el-button link type="primary" :icon="Plus" @click="addMcpServer">添加 MCP Server</el-button>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveAgent">保存</el-button>
      </template>
    </el-dialog>

    <!-- ── DNA 基因文件 Dialog ──────────────────────── -->
    <el-dialog v-model="dnaDialogVisible" title="DNA 基因文件" width="760px" :close-on-click-modal="false">
      <div class="dna-toolbar">
        <el-button type="primary" :icon="Plus" size="small" @click="openNewGeneDialog">新建文件</el-button>
      </div>
      <div v-if="dnaLoading" class="loading-wrap"><el-skeleton :rows="3" animated /></div>
      <el-empty v-else-if="geneFiles.length === 0" description="暂无 DNA 文件" :image-size="80" />
      <el-table v-else :data="geneFiles" style="width:100%" size="small">
        <el-table-column prop="category" label="分类" width="120" />
        <el-table-column prop="fileName" label="文件名" width="180" />
        <el-table-column label="内容预览">
          <template #default="{ row }">
            <span class="gene-preview">{{ truncate(row.content, 60) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="更新时间" width="140">
          <template #default="{ row }">{{ formatDate(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEditGeneDialog(row)">编辑</el-button>
            <el-button link type="danger" size="small" @click="deleteGene(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>

    <!-- ── 基因文件编辑 Dialog ─────────────────────── -->
    <el-dialog v-model="geneEditDialogVisible" :title="editingGene ? '编辑基因文件' : '新建基因文件'" width="640px">
      <el-form label-width="80px">
        <el-form-item label="文件名" required>
          <el-input v-model="geneForm.fileName" placeholder="如 personality.md" :disabled="!!editingGene" />
        </el-form-item>
        <el-form-item label="分类">
          <el-input v-model="geneForm.category" placeholder="如 persona（可选）" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input
            v-model="geneForm.content"
            type="textarea"
            :rows="10"
            placeholder="Markdown 格式，将注入 Agent SystemPrompt 上下文"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="geneEditDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="geneSaving" @click="saveGene">保存</el-button>
      </template>
    </el-dialog>

    <!-- ── MCP 工具预览 Dialog ─────────────────────── -->
    <el-dialog v-model="toolsDialogVisible" title="MCP 工具列表" width="560px">
      <div v-if="toolsLoading" class="loading-wrap"><el-skeleton :rows="3" animated /></div>
      <el-empty v-else-if="toolsList.length === 0" description="未加载到任何工具（请确认 MCP Server 配置正确）" :image-size="80" />
      <el-table v-else :data="toolsList" style="width:100%" size="small">
        <el-table-column prop="name" label="工具名" width="180" />
        <el-table-column prop="description" label="描述" />
      </el-table>
      <template #footer>
        <el-button @click="toolsDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Edit, Delete, Document, Tools, Promotion, Cpu } from '@element-plus/icons-vue'
import {
  listAgents, createAgent, updateAgent, deleteAgent,
  listAgentDna, writeAgentDna, deleteAgentDna, listAgentTools,
  listProviders, listChannels,
  type AgentConfig, type GeneFile, type McpTool, type McpServerConfig,
} from '@/services/gatewayApi'

// ── 列表状态 ──────────────────────────────────────────────────────────────────
const loading = ref(false)
const agents = ref<AgentConfig[]>([])
const providers = ref<Awaited<ReturnType<typeof listProviders>>>([])
const channels = ref<Awaited<ReturnType<typeof listChannels>>>([])

async function loadData() {
  loading.value = true
  try {
    ;[agents.value, providers.value, channels.value] = await Promise.all([
      listAgents(), listProviders(), listChannels(),
    ])
  } finally {
    loading.value = false
  }
}

onMounted(loadData)

function providerName(id: string) {
  return providers.value.find(p => p.id === id)?.displayName ?? ''
}

function truncate(str: string, len: number) {
  return str.length > len ? str.slice(0, len) + '…' : str
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false }).slice(0, 16)
}

// ── 启用/停用 ─────────────────────────────────────────────────────────────────
async function toggleEnabled(a: AgentConfig, val: boolean) {
  try {
    await updateAgent({ id: a.id, isEnabled: val })
    ElMessage.success(val ? '已启用' : '已停用')
  } catch {
    a.isEnabled = !val
    ElMessage.error('操作失败')
  }
}

// ── 创建/编辑 Dialog ──────────────────────────────────────────────────────────
interface McpServerFormItem extends McpServerConfig {
  argsText: string
}

interface AgentForm {
  name: string
  systemPrompt: string
  providerId: string
  isEnabled: boolean
  boundChannelIds: string[]
  mcpServers: McpServerFormItem[]
}

const dialogVisible = ref(false)
const saving = ref(false)
const editingAgent = ref<AgentConfig | null>(null)

const form = ref<AgentForm>({
  name: '',
  systemPrompt: '',
  providerId: '',
  isEnabled: true,
  boundChannelIds: [],
  mcpServers: [],
})

function openCreateDialog() {
  editingAgent.value = null
  form.value = { name: '', systemPrompt: '', providerId: '', isEnabled: true, boundChannelIds: [], mcpServers: [] }
  dialogVisible.value = true
}

function openEditDialog(a: AgentConfig) {
  editingAgent.value = a
  form.value = {
    name: a.name,
    systemPrompt: a.systemPrompt,
    providerId: a.providerId,
    isEnabled: a.isEnabled,
    boundChannelIds: [...a.boundChannelIds],
    mcpServers: a.mcpServers.map(s => ({
      ...s,
      argsText: s.args?.join(' ') ?? '',
    })),
  }
  dialogVisible.value = true
}

function addMcpServer() {
  form.value.mcpServers.push({
    name: '',
    transportType: 'stdio',
    command: '',
    args: null,
    env: null,
    url: null,
    argsText: '',
  })
}

function removeMcpServer(idx: number) {
  form.value.mcpServers.splice(idx, 1)
}

async function saveAgent() {
  if (!form.value.name.trim()) { ElMessage.warning('请填写 Agent 名称'); return }
  if (!form.value.providerId) { ElMessage.warning('请选择模型提供方'); return }

  saving.value = true
  try {
    const mcpServers: McpServerConfig[] = form.value.mcpServers.map(s => ({
      name: s.name,
      transportType: s.transportType,
      command: s.command || null,
      args: s.argsText ? s.argsText.trim().split(/\s+/) : null,
      env: s.env || null,
      url: s.url || null,
    }))

    if (editingAgent.value) {
      await updateAgent({
        id: editingAgent.value.id,
        name: form.value.name,
        systemPrompt: form.value.systemPrompt,
        providerId: form.value.providerId,
        isEnabled: form.value.isEnabled,
        boundChannelIds: form.value.boundChannelIds,
        mcpServers,
      })
      ElMessage.success('已保存')
    } else {
      await createAgent({
        name: form.value.name,
        systemPrompt: form.value.systemPrompt,
        providerId: form.value.providerId,
        isEnabled: form.value.isEnabled,
        boundChannelIds: form.value.boundChannelIds,
        mcpServers,
      })
      ElMessage.success('创建成功')
    }
    dialogVisible.value = false
    await loadData()
  } catch {
    ElMessage.error('保存失败')
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
    await loadData()
  } catch {
    ElMessage.error('删除失败')
  }
}

// ── DNA 基因文件 ───────────────────────────────────────────────────────────────
const dnaDialogVisible = ref(false)
const dnaLoading = ref(false)
const geneFiles = ref<GeneFile[]>([])
const currentAgentId = ref('')

async function openDnaDialog(a: AgentConfig) {
  currentAgentId.value = a.id
  dnaDialogVisible.value = true
  await loadGeneFiles()
}

async function loadGeneFiles() {
  dnaLoading.value = true
  try {
    geneFiles.value = await listAgentDna(currentAgentId.value)
  } finally {
    dnaLoading.value = false
  }
}

// ── 基因文件编辑 ──────────────────────────────────────────────────────────────
const geneEditDialogVisible = ref(false)
const geneSaving = ref(false)
const editingGene = ref<GeneFile | null>(null)
const geneForm = ref({ fileName: '', category: '', content: '' })

function openNewGeneDialog() {
  editingGene.value = null
  geneForm.value = { fileName: '', category: '', content: '' }
  geneEditDialogVisible.value = true
}

function openEditGeneDialog(g: GeneFile) {
  editingGene.value = g
  geneForm.value = { fileName: g.fileName, category: g.category, content: g.content }
  geneEditDialogVisible.value = true
}

async function saveGene() {
  if (!geneForm.value.fileName.trim()) { ElMessage.warning('请填写文件名'); return }
  geneSaving.value = true
  try {
    await writeAgentDna(currentAgentId.value, geneForm.value.fileName, geneForm.value.content, geneForm.value.category)
    ElMessage.success('已保存')
    geneEditDialogVisible.value = false
    await loadGeneFiles()
  } catch {
    ElMessage.error('保存失败')
  } finally {
    geneSaving.value = false
  }
}

async function deleteGene(g: GeneFile) {
  await ElMessageBox.confirm(`确定删除「${g.fileName}」？`, '删除确认', {
    confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning',
  })
  try {
    await deleteAgentDna(currentAgentId.value, g.fileName, g.category)
    ElMessage.success('已删除')
    await loadGeneFiles()
  } catch {
    ElMessage.error('删除失败')
  }
}

// ── MCP 工具预览 ──────────────────────────────────────────────────────────────
const toolsDialogVisible = ref(false)
const toolsLoading = ref(false)
const toolsList = ref<McpTool[]>([])

async function previewTools(a: AgentConfig) {
  toolsDialogVisible.value = true
  toolsLoading.value = true
  toolsList.value = []
  try {
    toolsList.value = await listAgentTools(a.id)
  } catch {
    ElMessage.error('连接 MCP Server 失败，请检查配置')
  } finally {
    toolsLoading.value = false
  }
}
</script>

<style scoped>
.page-container {
  padding: 24px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 24px;
}

.page-title {
  margin: 0 0 4px;
  font-size: 20px;
  font-weight: 600;
}

.page-desc {
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.loading-wrap {
  padding: 24px 0;
}

.agent-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.agent-card {
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  padding: 16px;
  background: var(--el-bg-color);
  display: flex;
  flex-direction: column;
  gap: 12px;
  transition: box-shadow 0.2s;
}

.agent-card:hover {
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.1);
}

.agent-card.disabled {
  opacity: 0.65;
}

.card-top {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.card-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.card-name {
  font-size: 15px;
  font-weight: 600;
}

.card-provider {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.card-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.card-prompt {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.4;
}

.card-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-top: 1px solid var(--el-border-color-lighter);
  padding-top: 10px;
}

.card-actions {
  display: flex;
  align-items: center;
}

.placeholder-icon {
  font-size: 72px;
  color: var(--el-text-color-placeholder);
}

.ml-1 {
  margin-left: 4px;
}

/* MCP Server 编辑 */
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

/* DNA */
.dna-toolbar {
  margin-bottom: 12px;
}

.gene-preview {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
