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
          <el-tag
            :type="row.transportType === 'sse' ? 'warning' : row.transportType === 'http' ? 'success' : 'info'"
            size="small"
          >
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
      width="620px"
      @close="resetForm"
    >
      <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="如：Filesystem、Web Search" />
        </el-form-item>

        <el-form-item label="JSON 配置" prop="jsonText">
          <div style="width: 100%">
            <el-input
              v-model="form.jsonText"
              type="textarea"
              :rows="12"
              placeholder='粘贴 MCP 配置 JSON，例如：
{
  "type": "stdio",
  "command": "npx",
  "args": ["-y", "@modelcontextprotocol/server-filesystem"]
}

或远程 HTTP 类型：
{
  "type": "http",
  "url": "https://example.com/mcp",
  "headers": { "Authorization": "Bearer xxx" }
}'
              style="font-family: monospace; font-size: 13px"
              @blur="validateJson"
            />
            <div v-if="jsonError" class="json-error">{{ jsonError }}</div>
            <div class="json-hint">支持字段：type（stdio / sse / http）、command、args、env、url、headers</div>
          </div>
        </el-form-item>

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
import type { McpServerConfig, McpToolInfo, McpTestResult, McpTransportType } from '@/services/gatewayApi'

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
const jsonError     = ref('')

const defaultForm = () => ({
  name: '',
  jsonText: '',
  isEnabled: true,
})
const form = ref(defaultForm())

const rules: FormRules = {
  name:     [{ required: true, message: '请输入名称', trigger: 'blur' }],
  jsonText: [{ required: true, message: '请粘贴 JSON 配置', trigger: 'blur' }],
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

// ── JSON 解析与验证 ───────────────────────────────────────────────────────────

function validateJson(): boolean {
  if (!form.value.jsonText.trim()) {
    jsonError.value = ''
    return false
  }
  try {
    JSON.parse(form.value.jsonText)
    jsonError.value = ''
    return true
  } catch (e: any) {
    jsonError.value = 'JSON 格式错误：' + e.message
    return false
  }
}

/** 将现有 McpServerConfig 反序列化为 JSON 字符串，供编辑框回填 */
function configToJson(row: McpServerConfig): string {
  const obj: Record<string, unknown> = { type: row.transportType }
  if (row.transportType === 'stdio') {
    if (row.command) obj.command = row.command
    if (row.args?.length) obj.args = row.args
    if (row.env && Object.keys(row.env).length) obj.env = row.env
  } else {
    if (row.url) obj.url = row.url
    if (row.headers && Object.keys(row.headers).length) obj.headers = row.headers
  }
  return JSON.stringify(obj, null, 2)
}

/** 从解析后的 JSON 对象提取 API 请求所需字段 */
function parseJsonToRequest(raw: Record<string, unknown>) {
  const t = (raw.type as string ?? 'stdio').toLowerCase()
  const transportType = (t === 'http' ? 'http' : t === 'sse' ? 'sse' : 'stdio') as McpTransportType

  if (transportType === 'stdio') {
    return {
      transportType,
      command: raw.command as string | undefined,
      args:    Array.isArray(raw.args) ? (raw.args as string[]) : undefined,
      env:     (raw.env && typeof raw.env === 'object' && !Array.isArray(raw.env))
                 ? (raw.env as Record<string, string>)
                 : undefined,
    }
  }
  return {
    transportType,
    url:     raw.url as string | undefined,
    headers: (raw.headers && typeof raw.headers === 'object' && !Array.isArray(raw.headers))
               ? (raw.headers as Record<string, string>)
               : undefined,
  }
}

// ── Dialog open/close ─────────────────────────────────────────────────────────

function openCreateDialog() {
  editingId.value     = null
  form.value          = defaultForm()
  jsonError.value     = ''
  dialogVisible.value = true
}

function openEditDialog(row: McpServerConfig) {
  editingId.value = row.id
  form.value = {
    name:      row.name,
    jsonText:  configToJson(row),
    isEnabled: row.isEnabled,
  }
  jsonError.value     = ''
  dialogVisible.value = true
}

function resetForm() {
  formRef.value?.clearValidate()
  form.value  = defaultForm()
  jsonError.value = ''
}

// ── Submit ────────────────────────────────────────────────────────────────────

async function submitForm() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  if (!validateJson()) return

  let parsed: Record<string, unknown>
  try {
    parsed = JSON.parse(form.value.jsonText)
  } catch {
    jsonError.value = 'JSON 解析失败，请检查格式'
    return
  }

  const fields = parseJsonToRequest(parsed)

  if (fields.transportType === 'stdio' && !fields.command) {
    jsonError.value = 'stdio 类型必须包含 "command" 字段'
    return
  }
  if ((fields.transportType === 'sse' || fields.transportType === 'http') && !fields.url) {
    jsonError.value = 'sse / http 类型必须包含 "url" 字段'
    return
  }

  saving.value = true
  try {
    if (editingId.value) {
      await updateMcpServer({ id: editingId.value, name: form.value.name, isEnabled: form.value.isEnabled, ...fields })
    } else {
      await createMcpServer({ name: form.value.name, isEnabled: form.value.isEnabled, ...fields })
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

.json-error {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-color-danger);
}

.json-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}
</style>
