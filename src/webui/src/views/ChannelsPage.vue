<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2 class="page-title">渠道</h2>
        <p class="page-desc">管理消息接入渠道，支持飞书、企业微信、微信等多渠道接入</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="openCreateDialog">添加渠道</el-button>
    </div>

    <div v-if="loading" class="loading-wrap">
      <el-skeleton :rows="3" animated />
    </div>

    <el-empty v-else-if="channels.length === 0" description="暂无渠道，点击右上角添加" :image-size="100">
      <template #image>
        <el-icon class="placeholder-icon"><Connection /></el-icon>
      </template>
    </el-empty>

    <div v-else class="channel-grid">
      <div v-for="c in channels" :key="c.id" class="channel-card" :class="{ disabled: !c.isEnabled }">
        <div class="card-top">
          <div class="card-title-row">
            <span class="card-name">{{ c.displayName }}</span>
            <el-tag :type="channelTagType(c.channelType)" size="small" class="channel-tag">
              {{ channelLabel(c.channelType) }}
            </el-tag>
          </div>
          <div class="card-provider">
            <el-icon><Cpu /></el-icon>
            {{ providerName(c.providerId) || c.providerId }}
          </div>
          <div class="card-mode">
            <el-tag v-if="parseConnectionMode(c) === 'websocket'" type="success" size="small">WebSocket 长连接</el-tag>
            <el-tag v-else type="info" size="small">Webhook 回调</el-tag>
          </div>
          <div v-if="parseConnectionMode(c) === 'webhook'" class="card-webhook">
            <el-icon><Link /></el-icon>
            <span class="webhook-url">{{ webhookUrl(c) }}</span>
            <el-button link size="small" @click="copyWebhook(c)">
              <el-icon><CopyDocument /></el-icon>
            </el-button>
          </div>
        </div>

        <div class="card-bottom">
          <el-switch
            v-model="c.isEnabled"
            size="small"
            active-text="启用"
            inactive-text="停用"
            @change="(val: boolean) => toggleEnabled(c, val)"
          />
          <div class="card-actions">
            <el-button
              link
              :loading="testResults[c.id]?.loading"
              @click="testConnection(c)"
            >测试</el-button>
            <el-divider direction="vertical" />
            <el-button
              v-if="c.channelType === 'feishu'"
              link
              type="success"
              @click="openPublishDialog(c)"
            >发送消息</el-button>
            <el-divider v-if="c.channelType === 'feishu'" direction="vertical" />
            <el-button link type="primary" :icon="Edit" @click="openEditDialog(c)">编辑</el-button>
            <el-divider direction="vertical" />
            <el-button link type="danger" :icon="Delete" @click="confirmDelete(c)">删除</el-button>
          </div>
        </div>
        <div
          v-if="testResults[c.id] && !testResults[c.id].loading"
          class="card-test-result"
        >
          <el-tag
            :type="testResults[c.id]?.success ? 'success' : 'danger'"
            size="small"
            effect="plain"
          >
            {{
              testResults[c.id]?.success
                ? `✓ 连通 ${testResults[c.id]?.latencyMs}ms`
                : `✗ ${testResults[c.id]?.message}`
            }}
          </el-tag>
        </div>
      </div>
    </div>

    <!-- 发送消息 Dialog -->
    <el-dialog
      v-model="publishDialogVisible"
      title="主动发送消息"
      width="480px"
      :close-on-click-modal="false"
      destroy-on-close
    >
      <el-form
        ref="publishFormRef"
        :model="publishForm"
        :rules="publishRules"
        label-position="top"
      >
        <el-form-item label="目标类型">
          <el-radio-group v-model="publishForm.targetType" @change="onTargetTypeChange">
            <el-radio value="user">用户（open_id）</el-radio>
            <el-radio value="group">群聊（chat_id）</el-radio>
          </el-radio-group>
          <div class="form-hint">
            {{ publishForm.targetType === 'user'
              ? '填写用户的 open_id，格式以 ou_ 开头' 
              : '填写群聊的 chat_id，格式以 oc_ 开头' }}
          </div>
        </el-form-item>

        <el-form-item
          :label="publishForm.targetType === 'user' ? 'Open ID' : 'Chat ID'"
          prop="targetId"
        >
          <el-input
            v-model="publishForm.targetId"
            :placeholder="publishForm.targetType === 'user' ? 'ou_xxxxxxxxxx' : 'oc_xxxxxxxxxx'"
          />
        </el-form-item>

        <el-form-item label="消息内容" prop="content">
          <el-input
            v-model="publishForm.content"
            type="textarea"
            :rows="5"
            placeholder="支持纯文本；含 Markdown 代码块、表格、标题时自动转换为飞书卡片格式"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="publishDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="publishSubmitting" @click="submitPublish">发送</el-button>
      </template>
    </el-dialog>

    <!-- 新增 / 编辑 Dialog -->
    <el-dialog
      v-model="dialogVisible"
      :title="isEditing ? '编辑渠道' : '添加渠道'"
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
        <!-- 基础配置 -->
        <div class="section-static">
          <div class="section-static-header">
            <span class="section-title">基础配置</span>
            <span class="section-subtitle">名称、渠道类型、关联模型</span>
          </div>
          <div class="section-body">
            <el-form-item label="显示名称" prop="displayName">
              <el-input v-model="form.displayName" placeholder="例如：飞书客服机器人" />
            </el-form-item>

            <el-form-item label="渠道类型" prop="channelType">
              <el-select v-model="form.channelType" style="width: 100%" :disabled="isEditing">
                <el-option label="飞书" value="feishu" />
                <el-option label="企业微信" value="wecom" disabled />
                <el-option label="微信" value="wechat" disabled />
              </el-select>
            </el-form-item>

            <el-form-item label="关联模型" prop="providerId">
              <el-select v-model="form.providerId" style="width: 100%" placeholder="选择一个 AI 模型提供方">
                <el-option
                  v-for="p in providers"
                  :key="p.id"
                  :label="`${p.displayName} (${p.modelName})`"
                  :value="p.id"
                  :disabled="!p.isEnabled"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="启用" class="form-item-inline">
              <el-switch v-model="form.isEnabled" />
            </el-form-item>
          </div>
        </div>

        <!-- 飞书特定配置 -->
        <div v-if="form.channelType === 'feishu'" class="section-static" style="margin-top: 8px">
          <div class="section-static-header section-header-feishu">
            <span class="section-title">飞书配置</span>
            <span class="section-subtitle">在飞书开放平台获取以下信息</span>
          </div>
          <div class="section-body">
            <el-form-item label="连接模式">
              <el-radio-group v-model="form.feishu.connectionMode">
                <el-radio value="websocket">WebSocket 长连接</el-radio>
                <el-radio value="webhook">Webhook 回调</el-radio>
              </el-radio-group>
              <div class="form-hint">
                {{ form.feishu.connectionMode === 'websocket'
                  ? '推荐：无需公网 IP，飞书主动推送消息到本服务'
                  : '需要公网可访问的 Webhook URL，飞书通过 HTTP POST 回调' }}
              </div>
            </el-form-item>

            <el-form-item label="App ID" prop="feishu.appId">
              <el-input v-model="form.feishu.appId" placeholder="飞书应用的 App ID" />
            </el-form-item>

            <el-form-item label="App Secret" prop="feishu.appSecret">
              <el-input
                v-model="form.feishu.appSecret"
                type="password"
                show-password
                :placeholder="isEditing ? '清空后重新输入新 Secret，不修改则保留原值' : '飞书应用的 App Secret'"
              />
            </el-form-item>

            <el-form-item v-if="form.feishu.connectionMode === 'webhook'" label="Encrypt Key">
              <el-input
                v-model="form.feishu.encryptKey"
                type="password"
                show-password
                :placeholder="isEditing ? '不修改则保留原值，清空后重新输入新密钥' : '事件订阅的加密密钥（可选）'"
              />
              <div class="form-hint">在飞书开放平台 → 事件订阅 → 加密策略中配置</div>
            </el-form-item>

            <el-form-item v-if="form.feishu.connectionMode === 'webhook'" label="Verification Token">
              <el-input
                v-model="form.feishu.verificationToken"
                type="password"
                show-password
                :placeholder="isEditing ? '不修改则保留原值，清空后重新输入新 Token' : '事件订阅的验证 Token（可选）'"
              />
            </el-form-item>

            <el-form-item label="API Base URL">
              <el-input
                v-model="form.feishu.apiBaseUrl"
                placeholder="https://open.feishu.cn（私有化部署时修改）"
              />
              <div class="form-hint">仅私有化部署或使用代理时需要修改，默认使用官方公有云地址</div>
            </el-form-item>
          </div>
        </div>
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
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { Plus, Edit, Delete, Cpu, Link, Connection, CopyDocument } from '@element-plus/icons-vue'
import {
  listChannels,
  createChannel,
  updateChannel,
  deleteChannel,
  testChannel,
  publishChannelMessage,
  listProviders,
} from '@/services/gatewayApi'
import type { ChannelConfig, ChannelType, ProviderConfig, ChannelTestResult } from '@/services/gatewayApi'

const loading = ref(false)
const submitting = ref(false)
const channels = ref<ChannelConfig[]>([])
const providers = ref<ProviderConfig[]>([])

// ── 发送消息 Dialog ──────────────────────────────────────────────────────────
const publishDialogVisible = ref(false)
const publishSubmitting = ref(false)
const publishingChannelId = ref('')
const publishFormRef = ref<FormInstance>()
const publishForm = reactive({
  targetType: 'user' as 'user' | 'group',
  targetId: '',
  content: '',
})
const publishRules: FormRules = {
  targetId: [{ required: true, message: '请输入目标 ID', trigger: 'blur' }],
  content: [{ required: true, message: '请输入消息内容', trigger: 'blur' }],
}

type TestState = { loading: boolean } & Partial<ChannelTestResult>
const testResults = reactive<Record<string, TestState>>({})

const dialogVisible = ref(false)
const isEditing = ref(false)
const editingId = ref('')
const formRef = ref<FormInstance>()

const form = reactive({
  displayName: '',
  channelType: 'feishu' as ChannelType,
  providerId: '',
  isEnabled: true,
  feishu: {
    appId: '',
    appSecret: '',
    encryptKey: '',
    verificationToken: '',
    connectionMode: 'websocket' as 'websocket' | 'webhook',
    apiBaseUrl: '',
  },
})

const rules: FormRules = {
  displayName: [{ required: true, message: '请输入显示名称', trigger: 'blur' }],
  channelType: [{ required: true, message: '请选择渠道类型', trigger: 'change' }],
  providerId: [{ required: true, message: '请选择关联模型', trigger: 'change' }],
  'feishu.appId': [{ required: true, message: '请输入 App ID', trigger: 'blur' }],
  'feishu.appSecret': [
    { required: true, message: '请输入 App Secret', trigger: 'blur' },
  ],
}

function channelLabel(type: ChannelType): string {
  const labels: Record<ChannelType, string> = {
    web: 'Web',
    feishu: '飞书',
    wecom: '企业微信',
    wechat: '微信',
  }
  return labels[type] ?? type
}

function channelTagType(type: ChannelType): 'primary' | 'success' | 'warning' {
  const types: Record<ChannelType, 'primary' | 'success' | 'warning'> = {
    web: 'primary',
    feishu: 'primary',
    wecom: 'success',
    wechat: 'warning',
  }
  return types[type] ?? 'primary'
}

function providerName(providerId: string): string {
  const p = providers.value.find(p => p.id === providerId)
  return p ? `${p.displayName} (${p.modelName})` : ''
}

function webhookUrl(c: ChannelConfig): string {
  const base = window.location.origin
  return `${base}/api/channels/${c.channelType}/${c.id}/webhook`
}

function parseConnectionMode(c: ChannelConfig): 'websocket' | 'webhook' {
  try {
    const parsed = typeof c.settings === 'string' ? JSON.parse(c.settings) : c.settings
    return parsed?.connectionMode === 'webhook' ? 'webhook' : 'websocket'
  } catch {
    return 'websocket'
  }
}

async function copyWebhook(c: ChannelConfig) {
  try {
    await navigator.clipboard.writeText(webhookUrl(c))
    ElMessage.success('Webhook URL 已复制')
  } catch {
    ElMessage.error('复制失败')
  }
}

async function loadData() {
  loading.value = true
  try {
    const [channelList, providerList] = await Promise.all([listChannels(), listProviders()])
    channels.value = channelList
    providers.value = providerList
  } catch {
    // 失败由全局拦截器展示后端错误信息
  } finally {
    loading.value = false
  }
}

function resetForm() {
  Object.assign(form, {
    displayName: '',
    channelType: 'feishu' as ChannelType,
    providerId: '',
    isEnabled: true,
    feishu: { appId: '', appSecret: '', encryptKey: '', verificationToken: '', connectionMode: 'websocket' as 'websocket' | 'webhook', apiBaseUrl: '' },
  })
}

function openCreateDialog() {
  isEditing.value = false
  editingId.value = ''
  resetForm()
  dialogVisible.value = true
}

function openEditDialog(c: ChannelConfig) {
  isEditing.value = true
  editingId.value = c.id

  Object.assign(form, {
    displayName: c.displayName,
    channelType: c.channelType,
    providerId: c.providerId,
    isEnabled: c.isEnabled,
    feishu: { appId: '', appSecret: '', encryptKey: '', verificationToken: '', connectionMode: 'websocket' as 'websocket' | 'webhook' },
  })

  // 解析渠道特定设置
  if (c.channelType === 'feishu' && c.settings) {
    try {
      const parsed = typeof c.settings === 'string' ? JSON.parse(c.settings) : c.settings
      form.feishu.appId = parsed.appId ?? ''
      form.feishu.appSecret = '***' // 编辑时预填掩码值，留原值则保留原有 Secret
      form.feishu.encryptKey = parsed.encryptKey ?? ''   // API 返回已掩码值，不改则后端自动保留原值
      form.feishu.verificationToken = parsed.verificationToken ?? ''  // 同上
      form.feishu.connectionMode = parsed.connectionMode ?? 'websocket'
      form.feishu.apiBaseUrl = parsed.apiBaseUrl ?? ''
    } catch { /* ignore */ }
  }

  dialogVisible.value = true
}

function buildSettingsJson(): string {
  if (form.channelType === 'feishu') {
    return JSON.stringify({
      appId: form.feishu.appId,
      appSecret: form.feishu.appSecret || (isEditing.value ? '***' : ''),
      encryptKey: form.feishu.encryptKey,       // 如含 *** 后端自动保留原值
      verificationToken: form.feishu.verificationToken, // 同上
      connectionMode: form.feishu.connectionMode,
      apiBaseUrl: form.feishu.apiBaseUrl || undefined,
    })
  }
  return '{}'
}

async function submitForm() {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  const settings = buildSettingsJson()

  submitting.value = true
  try {
    if (isEditing.value) {
      await updateChannel({
        id: editingId.value,
        displayName: form.displayName,
        channelType: form.channelType,
        providerId: form.providerId,
        isEnabled: form.isEnabled,
        settings,
      })
      ElMessage.success('渠道已更新')
    } else {
      await createChannel({
        displayName: form.displayName,
        channelType: form.channelType,
        providerId: form.providerId,
        isEnabled: form.isEnabled,
        settings,
      })
      ElMessage.success('渠道已添加')
    }
    dialogVisible.value = false
    await loadData()
  } catch {
    // 失败由全局拦截器展示后端错误信息
  } finally {
    submitting.value = false
  }
}

async function toggleEnabled(c: ChannelConfig, val: boolean) {
  try {
    await updateChannel({
      id: c.id,
      displayName: c.displayName,
      channelType: c.channelType,
      providerId: c.providerId,
      isEnabled: val,
      settings: c.settings,
    })
  } catch {
    c.isEnabled = !val  // 回滚开关状态
  }
}

async function confirmDelete(c: ChannelConfig) {
  try {
    await ElMessageBox.confirm(
      `确认删除渠道「${c.displayName}」？此操作不可撤销。`,
      '删除确认',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消', confirmButtonClass: 'el-button--danger' },
    )
  } catch {
    return
  }

  try {
    await deleteChannel(c.id)
    ElMessage.success('已删除')
    await loadData()
  } catch {
    // 删除失败由全局拦截器展示后端错误信息
  }
}

function openPublishDialog(c: ChannelConfig) {
  publishingChannelId.value = c.id
  publishForm.targetType = 'user'
  publishForm.targetId = ''
  publishForm.content = ''
  publishDialogVisible.value = true
}

function onTargetTypeChange() {
  publishForm.targetId = ''
}

async function submitPublish() {
  if (!publishFormRef.value) return
  const valid = await publishFormRef.value.validate().catch(() => false)
  if (!valid) return

  publishSubmitting.value = true
  try {
    await publishChannelMessage(publishingChannelId.value, {
      targetId: publishForm.targetId.trim(),
      content: publishForm.content,
    })
    ElMessage.success('消息已发送')
    publishDialogVisible.value = false
  } catch {
    // 失败由全局拦截器展示后端错误信息
  } finally {
    publishSubmitting.value = false
  }
}

async function testConnection(c: ChannelConfig) {
  testResults[c.id] = { loading: true }
  try {
    const result = await testChannel(c.id)
    testResults[c.id] = { loading: false, ...result }
    if (result.success) {
      ElMessage.success(`${c.displayName} 连接正常，延迟 ${result.latencyMs}ms`)
    } else {
      ElMessage.warning(`${c.displayName} 连接失败：${result.message}`)
    }
  } catch {
    testResults[c.id] = { loading: false, success: false, message: '请求失败', latencyMs: 0 }
  }
}

onMounted(loadData)
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

.channel-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: 16px;
}

.channel-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 18px 20px 14px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  transition: box-shadow 0.2s;
}

.channel-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
}

.channel-card.disabled {
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

.channel-tag {
  flex-shrink: 0;
}

.card-provider {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 13px;
  color: #6b7280;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-provider .el-icon {
  flex-shrink: 0;
  font-size: 13px;
}

.card-mode {
  display: flex;
  align-items: center;
  gap: 5px;
}

.card-webhook {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: #9ca3af;
}

.card-webhook .el-icon {
  flex-shrink: 0;
  font-size: 12px;
}

.webhook-url {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: 'SF Mono', 'Fira Code', monospace;
}

.card-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 10px;
  border-top: 1px solid #f3f4f6;
}

.card-test-result {
  padding: 6px 0 0;
  font-size: 12px;
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

.section-header-feishu {
  background: #f0fdf4;
  color: #16a34a;
}

.section-header-feishu .section-subtitle {
  color: #86efac;
}

.section-static-header .section-subtitle {
  color: #93c5fd;
}

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

.section-body {
  border: 1px solid #e5e7eb;
  border-top: none;
  border-radius: 0 0 6px 6px;
  padding: 14px 16px;
  margin-bottom: 4px;
}
</style>
