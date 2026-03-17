<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2 class="page-title">模型</h2>
        <p class="page-desc">管理 AI 模型提供方，支持 OpenAI、OpenAI Responses、Anthropic 协议</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="openCreateDialog">添加提供方</el-button>
    </div>

    <div v-if="loading" class="loading-wrap">
      <el-skeleton :rows="3" animated />
    </div>

    <el-empty v-else-if="providers.length === 0" description="暂无提供方，点击右上角添加" :image-size="100">
      <template #image>
        <el-icon class="placeholder-icon"><Cpu /></el-icon>
      </template>
    </el-empty>

    <div v-else class="provider-grid">
      <div v-for="p in providers" :key="p.id" class="provider-card" :class="{ disabled: !p.isEnabled }">
        <div class="card-top">
          <div class="card-title-row">
            <span class="card-name">{{ p.displayName }}</span>
            <el-tag :type="protocolTagType(p.protocol)" size="small" class="protocol-tag">
              {{ protocolLabel(p.protocol) }}
            </el-tag>
          </div>
          <div class="card-model">
            <el-icon><Cpu /></el-icon>
            {{ p.modelName }}
          </div>
          <div v-if="p.baseUrl" class="card-url">
            <el-icon><Link /></el-icon>
            {{ p.baseUrl }}
          </div>
        </div>

        <div class="card-bottom">
          <el-switch
            v-model="p.isEnabled"
            size="small"
            active-text="启用"
            inactive-text="停用"
            @change="(val: boolean) => toggleEnabled(p, val)"
          />
          <div class="card-actions">
            <el-button link type="primary" :icon="Edit" @click="openEditDialog(p)">编辑</el-button>
            <el-divider direction="vertical" />
            <el-button link type="danger" :icon="Delete" @click="confirmDelete(p)">删除</el-button>
          </div>
        </div>
      </div>
    </div>

    <!-- 新增 / 编辑 Dialog -->
    <el-dialog
      v-model="dialogVisible"
      :title="isEditing ? '编辑提供方' : '添加提供方'"
      width="520px"
      :close-on-click-modal="false"
      destroy-on-close
    >
      <el-form
        ref="formRef"
        :model="form"
        :rules="rules"
        label-width="110px"
        label-position="right"
      >
        <el-form-item label="显示名称" prop="displayName">
          <el-input v-model="form.displayName" placeholder="例如：My GPT-4o" />
        </el-form-item>

        <el-form-item label="协议类型" prop="protocol">
          <el-select v-model="form.protocol" style="width: 100%">
            <el-option label="OpenAI (Chat Completions)" value="openai" />
            <el-option label="OpenAI Responses API" value="openai-responses" />
            <el-option label="Anthropic (Claude)" value="anthropic" />
          </el-select>
        </el-form-item>

        <el-form-item label="Base URL" prop="baseUrl">
          <el-input
            v-model="form.baseUrl"
            placeholder="留空使用默认端点"
            clearable
          />
          <div class="form-hint">留空使用官方默认端点，填写可接入兼容 API 或代理</div>
        </el-form-item>

        <el-form-item label="API Key" prop="apiKey">
          <el-input
            v-model="form.apiKey"
            type="password"
            show-password
            :placeholder="isEditing ? '留空保留原有 Key' : '请输入 API Key'"
          />
        </el-form-item>

        <el-form-item label="模型名称" prop="modelName">
          <el-input v-model="form.modelName" :placeholder="modelNamePlaceholder" />
        </el-form-item>

        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitForm">
          {{ isEditing ? '保存' : '添加' }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { Plus, Edit, Delete, Cpu, Link } from '@element-plus/icons-vue'
import {
  listProviders,
  createProvider,
  updateProvider,
  deleteProvider,
} from '@/services/gatewayApi'
import type { ProviderConfig, ProviderProtocol } from '@/services/gatewayApi'

const loading = ref(false)
const submitting = ref(false)
const providers = ref<ProviderConfig[]>([])

const dialogVisible = ref(false)
const isEditing = ref(false)
const editingId = ref('')
const formRef = ref<FormInstance>()

const form = reactive({
  displayName: '',
  protocol: 'openai' as ProviderProtocol,
  baseUrl: '',
  apiKey: '',
  modelName: '',
  isEnabled: true,
})

const rules: FormRules = {
  displayName: [{ required: true, message: '请输入显示名称', trigger: 'blur' }],
  protocol: [{ required: true, message: '请选择协议类型', trigger: 'change' }],
  apiKey: [
    {
      validator: (_rule: unknown, value: string, callback: (err?: Error) => void) => {
        if (!isEditing.value && !value) {
          callback(new Error('请输入 API Key'))
        } else {
          callback()
        }
      },
      trigger: 'blur',
    },
  ],
  modelName: [{ required: true, message: '请输入模型名称', trigger: 'blur' }],
}

const modelNamePlaceholder = computed(() => {
  const placeholders: Record<ProviderProtocol, string> = {
    openai: 'gpt-4o',
    'openai-responses': 'gpt-4o',
    anthropic: 'claude-opus-4-5',
  }
  return placeholders[form.protocol] ?? 'gpt-4o'
})

function protocolLabel(protocol: ProviderProtocol): string {
  const labels: Record<ProviderProtocol, string> = {
    openai: 'OpenAI',
    'openai-responses': 'OAI Responses',
    anthropic: 'Anthropic',
  }
  return labels[protocol] ?? protocol
}

function protocolTagType(protocol: ProviderProtocol): 'success' | 'warning' | 'primary' {
  const types: Record<ProviderProtocol, 'success' | 'warning' | 'primary'> = {
    openai: 'success',
    'openai-responses': 'warning',
    anthropic: 'primary',
  }
  return types[protocol] ?? 'primary'
}

async function loadProviders() {
  loading.value = true
  try {
    providers.value = await listProviders()
  } catch {
    ElMessage.error('加载提供方列表失败')
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  isEditing.value = false
  editingId.value = ''
  Object.assign(form, {
    displayName: '',
    protocol: 'openai' as ProviderProtocol,
    baseUrl: '',
    apiKey: '',
    modelName: '',
    isEnabled: true,
  })
  dialogVisible.value = true
}

function openEditDialog(p: ProviderConfig) {
  isEditing.value = true
  editingId.value = p.id
  Object.assign(form, {
    displayName: p.displayName,
    protocol: p.protocol,
    baseUrl: p.baseUrl ?? '',
    apiKey: '',
    modelName: p.modelName,
    isEnabled: p.isEnabled,
  })
  dialogVisible.value = true
}

async function submitForm() {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  submitting.value = true
  try {
    if (isEditing.value) {
      await updateProvider({
        id: editingId.value,
        displayName: form.displayName,
        protocol: form.protocol,
        baseUrl: form.baseUrl || undefined,
        apiKey: form.apiKey || undefined,
        modelName: form.modelName,
        isEnabled: form.isEnabled,
      })
      ElMessage.success('提供方已更新')
    } else {
      await createProvider({
        displayName: form.displayName,
        protocol: form.protocol,
        baseUrl: form.baseUrl || undefined,
        apiKey: form.apiKey,
        modelName: form.modelName,
        isEnabled: form.isEnabled,
      })
      ElMessage.success('提供方已添加')
    }
    dialogVisible.value = false
    await loadProviders()
  } catch {
    ElMessage.error(isEditing.value ? '更新失败，请重试' : '添加失败，请重试')
  } finally {
    submitting.value = false
  }
}

async function toggleEnabled(p: ProviderConfig, val: boolean) {
  try {
    await updateProvider({
      id: p.id,
      displayName: p.displayName,
      protocol: p.protocol,
      baseUrl: p.baseUrl ?? undefined,
      apiKey: undefined,
      modelName: p.modelName,
      isEnabled: val,
    })
  } catch {
    ElMessage.error('状态更新失败')
    p.isEnabled = !val
  }
}

async function confirmDelete(p: ProviderConfig) {
  await ElMessageBox.confirm(
    `确认删除提供方「${p.displayName}」？此操作不可撤销。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消', confirmButtonClass: 'el-button--danger' },
  ).catch(() => null)

  try {
    await deleteProvider(p.id)
    ElMessage.success('已删除')
    await loadProviders()
  } catch {
    ElMessage.error('删除失败，请重试')
  }
}

onMounted(loadProviders)
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
  margin-bottom: 28px;
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

.loading-wrap {
  padding: 24px 0;
}

.placeholder-icon {
  font-size: 80px;
  color: #d1d5db;
}

.provider-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.provider-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 18px 20px 14px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  transition: box-shadow 0.2s;
}

.provider-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
}

.provider-card.disabled {
  opacity: 0.55;
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
  font-size: 16px;
  font-weight: 600;
  color: #111827;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.protocol-tag {
  flex-shrink: 0;
}

.card-model,
.card-url {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 13px;
  color: #6b7280;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-model .el-icon,
.card-url .el-icon {
  flex-shrink: 0;
  font-size: 13px;
}

.card-url {
  font-size: 12px;
  color: #9ca3af;
}

.card-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 10px;
  border-top: 1px solid #f3f4f6;
}

.card-actions {
  display: flex;
  align-items: center;
}

.form-hint {
  font-size: 12px;
  color: #9ca3af;
  margin-top: 4px;
  line-height: 1.4;
}
</style>
