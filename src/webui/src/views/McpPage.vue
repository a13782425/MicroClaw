<template>
  <div class="mcp-page">
    <div class="page-header">
      <h2 class="page-title">MCP 服务器管理</h2>
      <el-button type="primary" :icon="Plus" @click="openCreateDialog">新增服务器</el-button>
    </div>

    <el-table
      v-loading="loading"
      :data="servers"
      stripe
      style="width: 100%"
      row-key="id"
      :expand-row-keys="expandedRows"
      @expand-change="handleExpandChange"
    >
      <el-table-column type="expand">
        <template #default="{ row }">
          <div class="tools-expand">
            <div v-if="toolsLoading[row.id]" class="tools-loading">
              <el-icon class="is-loading"><Loading /></el-icon> 加载工具列表…
            </div>
            <div v-else-if="toolsError[row.id]" class="tools-error">
              <el-icon><Warning /></el-icon> {{ toolsError[row.id] }}
            </div>
            <div v-else-if="(toolsMap[row.id] ?? []).length === 0" class="tools-empty">
              暂无工具（服务器可能未启用或未连接）
            </div>
            <el-descriptions v-else :column="1" border size="small">
              <el-descriptions-item
                v-for="tool in toolsMap[row.id]"
                :key="tool.name"
                :label="tool.name"
              >{{ tool.description || '—' }}</el-descriptions-item>
            </el-descriptions>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="名称" prop="name" min-width="160" />

      <el-table-column label="传输类型" width="110">
        <template #default="{ row }">
          <el-tag :type="row.transportType === 'sse' ? 'warning' : 'info'" size="small">
            {{ row.transportType.toUpperCase() }}
          </el-tag>
        </template>
      </el-table-column>

      <el-table-column label="命令 / URL" min-width="220">
        <template #default="{ row }">
          <span v-if="row.transportType === 'stdio'" class="mono">
            {{ row.command }} {{ (row.args ?? []).join(' ') }}
          </span>
          <span v-else class="mono">{{ row.url }}</span>
        </template>
      </el-table-column>

      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-switch
            v-model="row.isEnabled"
            size="small"
            @change="(val: boolean) => toggleEnabled(row, val)"
          />
        </template>
      </el-table-column>

      <el-table-column label="操作" width="240" fixed="right">
        <template #default="{ row }">
          <el-button
            link
            type="primary"
            size="small"
            :loading="testingId === row.id"
            @click="handleTest(row)"
          >测试连接</el-button>
          <el-divider direction="vertical" />
          <el-button link type="primary" :icon="Edit" size="small" @click="openEditDialog(row)">编辑</el-button>
          <el-divider direction="vertical" />
          <el-button link type="danger" :icon="Delete" size="small" @click="confirmDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 测试结果提示 -->
    <div v-if="testResult" class="test-result-bar" :class="testResult.success ? 'success' : 'error'">
      <template v-if="testResult.success">
        ✓ 连接成功，发现 {{ testResult.toolCount }} 个工具：{{ (testResult.toolNames ?? []).join('、') || '—' }}
      </template>
      <template v-else>✗ {{ testResult.error }}</template>
    </div>

    <!-- 创建/编辑对话框 -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingId ? '编辑 MCP 服务器' : '新增 MCP 服务器'"
      width="600px"
      @close="resetForm"
    >
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="如：Filesystem / Playwright" />
        </el-form-item>

        <el-form-item label="传输类型" prop="transportType">
          <el-radio-group v-model="form.transportType">
            <el-radio value="stdio">stdio（本地进程）</el-radio>
            <el-radio value="sse">SSE（HTTP 远程）</el-radio>
          </el-radio-group>
        </el-form-item>

        <template v-if="form.transportType === 'stdio'">
          <el-form-item label="命令" prop="command">
            <el-input v-model="form.command" placeholder="如：npx、python、node" />
          </el-form-item>
          <el-form-item label="参数">
            <el-input
              v-model="form.argsText"
              type="textarea"
              :rows="2"
              placeholder="每行一个参数，如：&#10;-y&#10;@modelcontextprotocol/server-filesystem&#10;/workspace"
            />
          </el-form-item>
          <el-form-item label="环境变量">
            <el-input
              v-model="form.envText"
              type="textarea"
              :rows="3"
              placeholder="每行 KEY=VALUE，如：&#10;API_KEY=xxx&#10;NODE_ENV=production"
            />
          </el-form-item>
        </template>

        <template v-else>
          <el-form-item label="URL" prop="url">
            <el-input v-model="form.url" placeholder="如：http://localhost:8080/sse" />
          </el-form-item>
        </template>

        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitForm">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Edit, Delete, Loading, Warning } from '@element-plus/icons-vue'
import type { FormInstance, FormRules } from 'element-plus'
import {
  listMcpServers,
  createMcpServer,
  updateMcpServer,
  deleteMcpServer,
  testMcpServer,
  listMcpServerTools,
} from '@/services/gatewayApi'
import type { McpServerConfig, McpToolInfo, McpTestResult } from '@/services/gatewayApi'

// ── State ─────────────────────────────────────────────────────────────────────

const servers  = ref<McpServerConfig[]>([])
const loading  = ref(false)
const saving   = ref(false)
const testingId  = ref<string | null>(null)
const testResult = ref<McpTestResult | null>(null)

const expandedRows  = ref<string[]>([])
const toolsMap      = ref<Record<string, McpToolInfo[]>>({})
const toolsLoading  = ref<Record<string, boolean>>({})
const toolsError    = ref<Record<string, string>>({})

// ── Dialog ────────────────────────────────────────────────────────────────────

const dialogVisible = ref(false)
const editingId     = ref<string | null>(null)
const formRef       = ref<FormInstance>()

const defaultForm = () => ({
  name: '',
  transportType: 'stdio' as 'stdio' | 'sse',
  command: '',
  argsText: '',
  envText: '',
  url: '',
  isEnabled: true,
})
const form = ref(defaultForm())

const rules: FormRules = {
  name:          [{ required: true, message: '请输入名称', trigger: 'blur' }],
  command:       [{ required: true, message: '请输入命令', trigger: 'blur' }],
  url:           [{ required: true, message: '请输入 URL', trigger: 'blur' }],
}

// ── Load ──────────────────────────────────────────────────────────────────────

async function loadServers() {
  loading.value = true
  try {
    servers.value = await listMcpServers()
  } finally {
    loading.value = false
  }
}

onMounted(loadServers)

// ── Expand（工具预览）────────────────────────────────────────────────────────

async function handleExpandChange(row: McpServerConfig, expanded: McpServerConfig[]) {
  const isNowExpanded = expanded.some(r => r.id === row.id)
  if (!isNowExpanded || toolsMap.value[row.id]) return

  toolsLoading.value[row.id] = true
  toolsError.value[row.id]   = ''
  try {
    toolsMap.value[row.id] = await listMcpServerTools(row.id)
  } catch (e: any) {
    toolsError.value[row.id] = e?.response?.data?.detail || e?.message || '加载失败'
  } finally {
    toolsLoading.value[row.id] = false
  }
}

// ── Test ──────────────────────────────────────────────────────────────────────

async function handleTest(row: McpServerConfig) {
  testResult.value = null
  testingId.value  = row.id
  try {
    testResult.value = await testMcpServer(row.id)
    if (!testResult.value.success) {
      ElMessage.warning('连接失败：' + testResult.value.error)
    } else {
      ElMessage.success(`连接成功，发现 ${testResult.value.toolCount} 个工具`)
    }
  } catch {
    ElMessage.error('测试请求失败')
  } finally {
    testingId.value = null
  }
}

// ── Toggle enabled ────────────────────────────────────────────────────────────

async function toggleEnabled(row: McpServerConfig, val: boolean) {
  try {
    await updateMcpServer({ id: row.id, isEnabled: val })
  } catch {
    row.isEnabled = !val
    ElMessage.error('更新失败')
  }
}

// ── Dialog open/close ─────────────────────────────────────────────────────────

function openCreateDialog() {
  editingId.value    = null
  form.value         = defaultForm()
  dialogVisible.value = true
}

function openEditDialog(row: McpServerConfig) {
  editingId.value = row.id
  form.value = {
    name:          row.name,
    transportType: row.transportType,
    command:       row.command ?? '',
    argsText:      (row.args ?? []).join('\n'),
    envText:       Object.entries(row.env ?? {}).map(([k, v]) => `${k}=${v}`).join('\n'),
    url:           row.url ?? '',
    isEnabled:     row.isEnabled,
  }
  dialogVisible.value = true
}

function resetForm() {
  formRef.value?.clearValidate()
  form.value = defaultForm()
}

// ── Submit ────────────────────────────────────────────────────────────────────

function parseArgs(text: string): string[] {
  return text.split('\n').map(s => s.trim()).filter(Boolean)
}

function parseEnv(text: string): Record<string, string> {
  const result: Record<string, string> = {}
  text.split('\n').forEach(line => {
    const idx = line.indexOf('=')
    if (idx > 0) result[line.slice(0, idx).trim()] = line.slice(idx + 1).trim()
  })
  return result
}

async function submitForm() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    const args = parseArgs(form.value.argsText)
    const env  = parseEnv(form.value.envText)

    if (editingId.value) {
      await updateMcpServer({
        id:            editingId.value,
        name:          form.value.name,
        transportType: form.value.transportType,
        command:       form.value.transportType === 'stdio' ? form.value.command : undefined,
        args:          form.value.transportType === 'stdio' ? args : undefined,
        env:           form.value.transportType === 'stdio' && Object.keys(env).length ? env : undefined,
        url:           form.value.transportType === 'sse' ? form.value.url : undefined,
        isEnabled:     form.value.isEnabled,
      })
    } else {
      await createMcpServer({
        name:          form.value.name,
        transportType: form.value.transportType,
        command:       form.value.transportType === 'stdio' ? form.value.command : undefined,
        args:          form.value.transportType === 'stdio' ? args : undefined,
        env:           form.value.transportType === 'stdio' && Object.keys(env).length ? env : undefined,
        url:           form.value.transportType === 'sse' ? form.value.url : undefined,
        isEnabled:     form.value.isEnabled,
      })
    }

    ElMessage.success(editingId.value ? '更新成功' : '创建成功')
    dialogVisible.value = false
    await loadServers()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || '操作失败')
  } finally {
    saving.value = false
  }
}

// ── Delete ────────────────────────────────────────────────────────────────────

async function confirmDelete(row: McpServerConfig) {
  await ElMessageBox.confirm(`确定删除 MCP 服务器「${row.name}」？`, '删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    confirmButtonClass: 'el-button--danger',
  })
  try {
    await deleteMcpServer(row.id)
    ElMessage.success('已删除')
    await loadServers()
  } catch {
    ElMessage.error('删除失败')
  }
}
</script>

<style scoped>
.mcp-page {
  padding: 24px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}

.mono {
  font-family: monospace;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.tools-expand {
  padding: 12px 24px;
}

.tools-loading,
.tools-error,
.tools-empty {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.tools-error {
  color: var(--el-color-danger);
}

.test-result-bar {
  margin-top: 12px;
  padding: 10px 16px;
  border-radius: 6px;
  font-size: 13px;
}

.test-result-bar.success {
  background: var(--el-color-success-light-9);
  color: var(--el-color-success);
  border: 1px solid var(--el-color-success-light-7);
}

.test-result-bar.error {
  background: var(--el-color-danger-light-9);
  color: var(--el-color-danger);
  border: 1px solid var(--el-color-danger-light-7);
}
</style>
