<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2 class="page-title">模型</h2>
        <p class="page-desc">管理 AI 模型提供方，支持 OpenAI（兼容）和 Anthropic 协议</p>
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
          <div class="card-model" style="color: #6b7280; font-size: 13px;">
            最大输出 {{ p.maxOutputTokens.toLocaleString() }} tokens
          </div>
          <div v-if="p.baseUrl" class="card-url">
            <el-icon><Link /></el-icon>
            {{ p.baseUrl }}
          </div>
          <!-- 模态能力 badges -->
          <div class="card-modalities">
            <el-tooltip v-if="p.capabilities?.inputImage" content="支持图片输入" placement="top">
              <el-tag size="small" type="info" effect="plain">图片</el-tag>
            </el-tooltip>
            <el-tooltip v-if="p.capabilities?.inputAudio" content="支持音频输入" placement="top">
              <el-tag size="small" type="info" effect="plain">音频</el-tag>
            </el-tooltip>
            <el-tooltip v-if="p.capabilities?.inputVideo" content="支持视频输入" placement="top">
              <el-tag size="small" type="info" effect="plain">视频</el-tag>
            </el-tooltip>
            <el-tooltip v-if="p.capabilities?.inputFile" content="支持文件输入" placement="top">
              <el-tag size="small" type="info" effect="plain">文件</el-tag>
            </el-tooltip>
            <el-tooltip v-if="p.capabilities?.supportsFunctionCalling" content="支持 Function Calling" placement="top">
              <el-tag size="small" type="warning" effect="plain">Functions</el-tag>
            </el-tooltip>
            <el-tooltip v-if="p.capabilities?.supportsResponsesApi" content="支持 Responses API" placement="top">
              <el-tag size="small" type="success" effect="plain">Responses</el-tag>
            </el-tooltip>
            <template v-if="p.capabilities?.inputPricePerMToken != null || p.capabilities?.outputPricePerMToken != null">
              <span class="card-price">
                <template v-if="p.capabilities?.inputPricePerMToken != null">输入 ${{ p.capabilities.inputPricePerMToken }}/1M</template>
                <template v-if="p.capabilities?.inputPricePerMToken != null && p.capabilities?.outputPricePerMToken != null"> · </template>
                <template v-if="p.capabilities?.outputPricePerMToken != null">输出 ${{ p.capabilities.outputPricePerMToken }}/1M</template>
              </span>
            </template>
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
        label-position="top"
      >
        <!-- 基础配置（常开） -->
        <div class="section-static">
          <div class="section-static-header">
            <span class="section-title">基础配置</span>
            <span class="section-subtitle">名称、协议、密钥等</span>
          </div>
          <div class="section-body">
            <el-form-item label="显示名称" prop="displayName">
              <el-input v-model="form.displayName" placeholder="例如：My GPT-4o" />
            </el-form-item>

            <el-form-item label="协议类型" prop="protocol">
              <el-select v-model="form.protocol" style="width: 100%">
                <el-option label="OpenAI / OpenAI 兼容" value="openai" />
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

            <el-form-item label="最大输出 Tokens" prop="maxOutputTokens">
              <el-input-number
                v-model="form.maxOutputTokens"
                :min="256"
                :max="131072"
                :step="1024"
                controls-position="right"
                style="width: 100%"
              />
              <div class="form-hint">Anthropic 协议必填，OpenAI 兼容协议可选。默认 8192</div>
            </el-form-item>

            <el-form-item label="启用" class="form-item-inline">
              <el-switch v-model="form.isEnabled" />
            </el-form-item>
          </div>
        </div>

        <!-- 渐进式折叠：能力配置 + 价格备注 -->
        <el-collapse v-model="sectionCollapse" class="section-collapse">
          <!-- 能力配置 -->
          <el-collapse-item name="caps">
            <template #title>
              <span class="section-title">能力配置</span>
              <span class="section-subtitle">输入/输出模态、Function Calling 等</span>
            </template>
            <div class="section-body">
              <div class="field-group">
                <div class="field-title">输入模态</div>
                <el-checkbox-group v-model="form.inputModalities">
                  <el-checkbox value="inputImage">图片</el-checkbox>
                  <el-checkbox value="inputAudio">音频</el-checkbox>
                  <el-checkbox value="inputVideo">视频</el-checkbox>
                  <el-checkbox value="inputFile">文件</el-checkbox>
                </el-checkbox-group>
              </div>

              <div class="field-group">
                <div class="field-title">输出模态</div>
                <el-checkbox-group v-model="form.outputModalities">
                  <el-checkbox value="outputImage">图片</el-checkbox>
                  <el-checkbox value="outputAudio">音频</el-checkbox>
                  <el-checkbox value="outputVideo">视频</el-checkbox>
                </el-checkbox-group>
              </div>

              <div class="field-group">
                <div class="field-title">特殊能力</div>
                <div class="switches-row">
                  <div class="switch-item">
                    <el-switch v-model="form.supportsFunctionCalling" size="small" />
                    <span>Function Calling</span>
                  </div>
                  <div class="switch-item">
                    <el-switch v-model="form.supportsResponsesApi" size="small" />
                    <span>Responses API</span>
                  </div>
                </div>
              </div>
            </div>
          </el-collapse-item>

          <!-- 价格备注 -->
          <el-collapse-item name="price">
            <template #title>
              <span class="section-title">价格 &amp; 备注</span>
              <span class="section-subtitle">计费单价（$/1M tokens）及说明</span>
            </template>
            <div class="section-body">
              <div class="field-group">
                <div class="field-title">计费单价 <span class="field-hint">$/1M tokens</span></div>
                <div class="price-grid">
                  <div class="price-cell">
                    <span class="price-label">输入</span>
                    <el-input-number
                      v-model="form.inputPricePerMToken"
                      :precision="4" :step="0.1" :min="0"
                      controls-position="right"
                      style="width: 100%"
                    />
                  </div>
                  <div class="price-cell">
                    <span class="price-label">输出</span>
                    <el-input-number
                      v-model="form.outputPricePerMToken"
                      :precision="4" :step="0.1" :min="0"
                      controls-position="right"
                      style="width: 100%"
                    />
                  </div>
                  <div class="price-cell">
                    <span class="price-label">缓存输入</span>
                    <el-input-number
                      v-model="form.cacheInputPricePerMToken"
                      :precision="4" :step="0.1" :min="0"
                      controls-position="right"
                      style="width: 100%"
                    />
                  </div>
                  <div class="price-cell">
                    <span class="price-label">缓存输出</span>
                    <el-input-number
                      v-model="form.cacheOutputPricePerMToken"
                      :precision="4" :step="0.1" :min="0"
                      controls-position="right"
                      style="width: 100%"
                    />
                  </div>
                </div>
              </div>

              <div class="field-group">
                <div class="field-title">备注</div>
                <el-input
                  v-model="form.notes"
                  type="textarea"
                  :rows="2"
                  placeholder="模型说明、使用限制等"
                />
              </div>
            </div>
          </el-collapse-item>
        </el-collapse>
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
const sectionCollapse = ref<string[]>([])

const form = reactive({
  displayName: '',
  protocol: 'openai' as ProviderProtocol,
  baseUrl: '',
  apiKey: '',
  modelName: '',
  maxOutputTokens: 8192,
  isEnabled: true,
  // capabilities
  inputModalities: [] as string[],
  outputModalities: [] as string[],
  supportsFunctionCalling: false,
  supportsResponsesApi: false,
  inputPricePerMToken: undefined as number | undefined,
  outputPricePerMToken: undefined as number | undefined,
  cacheInputPricePerMToken: undefined as number | undefined,
  cacheOutputPricePerMToken: undefined as number | undefined,
  notes: '',
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
    anthropic: 'claude-opus-4-5',
  }
  return placeholders[form.protocol] ?? 'gpt-4o'
})

function protocolLabel(protocol: ProviderProtocol): string {
  const labels: Record<ProviderProtocol, string> = {
    openai: 'OpenAI',
    anthropic: 'Anthropic',
  }
  return labels[protocol] ?? protocol
}

function protocolTagType(protocol: ProviderProtocol): 'success' | 'primary' {
  const types: Record<ProviderProtocol, 'success' | 'primary'> = {
    openai: 'success',
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
    maxOutputTokens: 8192,
    isEnabled: true,
    inputModalities: [],
    outputModalities: [],
    supportsFunctionCalling: false,
    supportsResponsesApi: false,
    inputPricePerMToken: undefined,
    outputPricePerMToken: undefined,
    cacheInputPricePerMToken: undefined,
    cacheOutputPricePerMToken: undefined,
    notes: '',
  })
  dialogVisible.value = true
}

function openEditDialog(p: ProviderConfig) {
  isEditing.value = true
  editingId.value = p.id
  const cap = p.capabilities
  const inputModalities: string[] = []
  const outputModalities: string[] = []
  if (cap?.inputImage) inputModalities.push('inputImage')
  if (cap?.inputAudio) inputModalities.push('inputAudio')
  if (cap?.inputVideo) inputModalities.push('inputVideo')
  if (cap?.inputFile) inputModalities.push('inputFile')
  if (cap?.outputImage) outputModalities.push('outputImage')
  if (cap?.outputAudio) outputModalities.push('outputAudio')
  if (cap?.outputVideo) outputModalities.push('outputVideo')
  Object.assign(form, {
    displayName: p.displayName,
    protocol: p.protocol,
    baseUrl: p.baseUrl ?? '',
    apiKey: '',
    modelName: p.modelName,
    maxOutputTokens: p.maxOutputTokens ?? 8192,
    isEnabled: p.isEnabled,
    inputModalities,
    outputModalities,
    supportsFunctionCalling: cap?.supportsFunctionCalling ?? false,
    supportsResponsesApi: cap?.supportsResponsesApi ?? false,
    inputPricePerMToken: cap?.inputPricePerMToken ?? undefined,
    outputPricePerMToken: cap?.outputPricePerMToken ?? undefined,
    cacheInputPricePerMToken: cap?.cacheInputPricePerMToken ?? undefined,
    cacheOutputPricePerMToken: cap?.cacheOutputPricePerMToken ?? undefined,
    notes: cap?.notes ?? '',
  })
  dialogVisible.value = true
}

async function submitForm() {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  const capabilities = {
    inputText: true,
    inputImage: form.inputModalities.includes('inputImage'),
    inputAudio: form.inputModalities.includes('inputAudio'),
    inputVideo: form.inputModalities.includes('inputVideo'),
    inputFile: form.inputModalities.includes('inputFile'),
    outputText: true,
    outputImage: form.outputModalities.includes('outputImage'),
    outputAudio: form.outputModalities.includes('outputAudio'),
    outputVideo: form.outputModalities.includes('outputVideo'),
    supportsFunctionCalling: form.supportsFunctionCalling,
    supportsResponsesApi: form.supportsResponsesApi,
    inputPricePerMToken: form.inputPricePerMToken ?? null,
    outputPricePerMToken: form.outputPricePerMToken ?? null,
    cacheInputPricePerMToken: form.cacheInputPricePerMToken ?? null,
    cacheOutputPricePerMToken: form.cacheOutputPricePerMToken ?? null,
    notes: form.notes || null,
  }

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
        maxOutputTokens: form.maxOutputTokens,
        isEnabled: form.isEnabled,
        capabilities,
      })
      ElMessage.success('提供方已更新')
    } else {
      await createProvider({
        displayName: form.displayName,
        protocol: form.protocol,
        baseUrl: form.baseUrl || undefined,
        apiKey: form.apiKey,
        modelName: form.modelName,
        maxOutputTokens: form.maxOutputTokens,
        isEnabled: form.isEnabled,
        capabilities,
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
      maxOutputTokens: p.maxOutputTokens,
      isEnabled: val,
    })
  } catch {
    ElMessage.error('状态更新失败')
    p.isEnabled = !val
  }
}

async function confirmDelete(p: ProviderConfig) {
  try {
    await ElMessageBox.confirm(
      `确认删除提供方「${p.displayName}」？此操作不可撤销。`,
      '删除确认',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消', confirmButtonClass: 'el-button--danger' },
    )
  } catch {
    return
  }

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

.card-modalities {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 4px;
}

.card-price {
  font-size: 11px;
  color: #9ca3af;
  align-self: center;
  white-space: nowrap;
}

.switches-row {
  display: flex;
  gap: 24px;
}

.switch-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #374151;
}

/* 基础配置（常开 section） */
.section-static {
  margin-bottom: 8px;
}

.section-static-header {
  height: 40px;
  line-height: 40px;
  background: #eff6ff;
  color: #2563eb;
  border-radius: 6px 6px 0 0;
  padding: 0 12px;
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-static-header .section-subtitle {
  color: #93c5fd;
}

/* section-body 内 el-form-item 适配 label-position="top" */
.section-body :deep(.el-form-item) {
  margin-bottom: 12px;
}

.section-body :deep(.el-form-item:last-child) {
  margin-bottom: 0;
}

.section-body :deep(.el-form-item__label) {
  font-size: 13px;
  font-weight: 600;
  color: #1f2937;
  padding-bottom: 4px;
}

.form-item-inline :deep(.el-form-item__label) {
  padding-bottom: 0;
}

/* 渐进式折叠 */
.section-collapse {
  border-left: none;
  border-right: none;
  margin-top: 8px;
}

.section-collapse :deep(.el-collapse-item__header) {
  height: 40px;
  line-height: 40px;
  background: #f9fafb;
  border-radius: 6px;
  padding: 0 12px;
  margin-bottom: 2px;
  font-size: 13px;
  color: #374151;
  border-bottom: none;
  gap: 8px;
  flex-wrap: nowrap;
  overflow: hidden;
}

.section-collapse :deep(.el-collapse-item__header.is-active) {
  background: #eff6ff;
  color: #2563eb;
  border-radius: 6px 6px 0 0;
}

.section-collapse :deep(.el-collapse-item__arrow) {
  margin-left: auto;
}

.section-collapse :deep(.el-collapse-item__wrap) {
  border-bottom: none;
  background: transparent;
}

.section-collapse :deep(.el-collapse-item__content) {
  padding: 0;
}

.section-title {
  font-size: 13px;
  font-weight: 500;
  color: inherit;
}

.section-subtitle {
  font-size: 12px;
  color: #9ca3af;
  margin-left: 8px;
  font-weight: 400;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.section-collapse :deep(.el-collapse-item__header.is-active) .section-subtitle {
  color: #93c5fd;
}

.section-body {
  border: 1px solid #e5e7eb;
  border-top: none;
  border-radius: 0 0 6px 6px;
  padding: 14px 16px;
  margin-bottom: 4px;
}

.field-group {
  margin-bottom: 14px;
}

.field-group:last-child {
  margin-bottom: 0;
}

.field-title {
  font-size: 13px;
  font-weight: 600;
  color: #1f2937;
  margin-bottom: 8px;
}

.field-hint {
  font-size: 12px;
  font-weight: 400;
  color: #9ca3af;
  margin-left: 6px;
}

/* 价格 2×2 网格 */
.price-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  width: 100%;
}

.price-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.price-label {
  font-size: 11px;
  color: #6b7280;
  line-height: 1;
}

</style>
