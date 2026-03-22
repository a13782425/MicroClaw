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
            <span v-if="c.channelType === 'feishu'" :class="healthDotClass(c.id)" :title="healthLevel(c.id) === 'ok' ? '运行正常' : healthLevel(c.id) === 'warn' ? '最近消息处理失败' : healthLevel(c.id) === 'error' ? '连接已断开' : '状态未知'"></span>
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

          <!-- F-F-3: 错误事件统计数字卡 -->
          <div v-if="c.channelType === 'feishu' && channelStats[c.id]" class="card-stats">
            <div class="stat-item" :class="{ 'stat-error': channelStats[c.id].signatureFailures > 0 }">
              <span class="stat-label">签名失败</span>
              <span class="stat-value">{{ channelStats[c.id].signatureFailures }}</span>
            </div>
            <div class="stat-item" :class="{ 'stat-error': channelStats[c.id].aiCallFailures > 0 }">
              <span class="stat-label">AI 失败</span>
              <span class="stat-value">{{ channelStats[c.id].aiCallFailures }}</span>
            </div>
            <div class="stat-item" :class="{ 'stat-error': channelStats[c.id].replyFailures > 0 }">
              <span class="stat-label">回复失败</span>
              <span class="stat-value">{{ channelStats[c.id].replyFailures }}</span>
            </div>
          </div>

          <!-- F-G-4: 最近处理时间 -->
          <div v-if="c.channelType === 'feishu' && channelHealth[c.id]?.lastMessageAt" class="card-last-message">
            <el-icon><Clock /></el-icon>
            <span>最近处理：{{ formatRelativeTime(channelHealth[c.id].lastMessageAt) }}</span>
            <el-tag
              v-if="channelHealth[c.id].lastMessageSuccess !== null"
              :type="channelHealth[c.id].lastMessageSuccess ? 'success' : 'danger'"
              size="small"
              effect="plain"
            >{{ channelHealth[c.id].lastMessageSuccess ? '成功' : '失败' }}</el-tag>
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
            <el-button
              v-if="c.channelType === 'feishu'"
              link
              type="info"
              @click="openHealthDrawer(c)"
            >健康详情</el-button>
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
          <!-- F-E-3: Webhook 内网探测提示 -->
          <el-alert
            v-if="testResults[c.id]?.connectivityHint"
            :title="testResults[c.id]?.connectivityHint"
            type="warning"
            show-icon
            :closable="false"
            class="card-connectivity-hint"
          />
        </div>
      </div>
    </div>

    <!-- F-G-4: 渠道健康仪表盘 Drawer -->
    <el-drawer
      v-model="healthDrawerVisible"
      title="渠道健康详情"
      size="400px"
      direction="rtl"
      destroy-on-close
    >
      <template v-if="healthDrawerChannel">
        <div class="health-drawer-content">
          <!-- 渠道基本信息 -->
          <div class="health-section">
            <div class="health-section-title">基本信息</div>
            <div class="health-row">
              <span class="health-label">渠道名称</span>
              <span class="health-value">{{ healthDrawerChannel.displayName }}</span>
            </div>
            <div class="health-row">
              <span class="health-label">渠道 ID</span>
              <span class="health-value health-mono">{{ healthDrawerChannel.id }}</span>
            </div>
          </div>

          <!-- 连接状态 -->
          <div class="health-section">
            <div class="health-section-title">连接状态</div>
            <div v-if="channelHealth[healthDrawerChannel.id]" class="health-rows">
              <div class="health-row">
                <span class="health-label">连接模式</span>
                <el-tag size="small" :type="channelHealth[healthDrawerChannel.id].connectionMode === 'websocket' ? 'success' : 'info'">
                  {{ channelHealth[healthDrawerChannel.id].connectionMode === 'websocket' ? 'WebSocket 长连接' : 'Webhook 回调' }}
                </el-tag>
              </div>
              <div class="health-row">
                <span class="health-label">连接状态</span>
                <el-tag
                  size="small"
                  :type="connectionStatusLabel(channelHealth[healthDrawerChannel.id].connectionStatus).type"
                >
                  {{ connectionStatusLabel(channelHealth[healthDrawerChannel.id].connectionStatus).label }}
                </el-tag>
              </div>
              <div class="health-row">
                <span class="health-label">Token 有效期</span>
                <span class="health-value">{{ formatTokenTtl(channelHealth[healthDrawerChannel.id].tokenRemainingSeconds) }}</span>
              </div>
            </div>
            <div v-else class="health-empty">暂无健康数据</div>
          </div>

          <!-- 最近消息 -->
          <div class="health-section">
            <div class="health-section-title">最近消息</div>
            <div v-if="channelHealth[healthDrawerChannel.id]?.lastMessageAt" class="health-rows">
              <div class="health-row">
                <span class="health-label">处理时间</span>
                <span class="health-value">{{ formatRelativeTime(channelHealth[healthDrawerChannel.id].lastMessageAt) }}</span>
              </div>
              <div class="health-row">
                <span class="health-label">处理结果</span>
                <el-tag
                  size="small"
                  :type="channelHealth[healthDrawerChannel.id].lastMessageSuccess ? 'success' : 'danger'"
                >
                  {{ channelHealth[healthDrawerChannel.id].lastMessageSuccess ? '成功' : '失败' }}
                </el-tag>
              </div>
              <div v-if="channelHealth[healthDrawerChannel.id].lastMessageError" class="health-row health-row-col">
                <span class="health-label">错误详情</span>
                <span class="health-value health-error-text">{{ channelHealth[healthDrawerChannel.id].lastMessageError }}</span>
              </div>
            </div>
            <div v-else class="health-empty">暂无消息记录</div>
          </div>

          <!-- 错误统计 -->
          <div class="health-section">
            <div class="health-section-title">错误统计（本次运行累计）</div>
            <div v-if="channelStats[healthDrawerChannel.id]" class="health-stat-grid">
              <div class="health-stat-card" :class="{ 'health-stat-error': channelStats[healthDrawerChannel.id].signatureFailures > 0 }">
                <span class="health-stat-num">{{ channelStats[healthDrawerChannel.id].signatureFailures }}</span>
                <span class="health-stat-desc">签名验证失败</span>
              </div>
              <div class="health-stat-card" :class="{ 'health-stat-error': channelStats[healthDrawerChannel.id].aiCallFailures > 0 }">
                <span class="health-stat-num">{{ channelStats[healthDrawerChannel.id].aiCallFailures }}</span>
                <span class="health-stat-desc">AI 调用失败</span>
              </div>
              <div class="health-stat-card" :class="{ 'health-stat-error': channelStats[healthDrawerChannel.id].replyFailures > 0 }">
                <span class="health-stat-num">{{ channelStats[healthDrawerChannel.id].replyFailures }}</span>
                <span class="health-stat-desc">回复发送失败</span>
              </div>
            </div>
            <div v-else class="health-empty">暂无统计数据</div>
          </div>

          <!-- 刷新按钮 -->
          <div class="health-drawer-footer">
            <el-button
              type="primary"
              plain
              size="small"
              @click="refreshHealthDrawer"
            >刷新数据</el-button>
          </div>
        </div>
      </template>
    </el-drawer>

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

        <!-- F-G-3: 云文档工具配置 -->
        <div v-if="form.channelType === 'feishu'" class="section-static" style="margin-top: 8px">
          <div class="section-static-header section-header-doc">
            <span class="section-title">云文档工具访问控制</span>
            <span class="section-subtitle">限制 Agent 可访问的文档范围（留空不限制）</span>
          </div>
          <div class="section-body">
            <el-form-item label="文档 Token 白名单">
              <el-input
                v-model="form.feishu.allowedDocTokens"
                type="textarea"
                :rows="2"
                placeholder="多个 Token 用英文逗号分隔，留空则 Agent 可访问任意可见文档"
              />
              <div class="form-hint">
                read_feishu_doc 和 write_feishu_doc 工具仅允许访问列出的文档 Token（如 <code>doxcnXXXXX</code>）。留空表示不限制。
              </div>
            </el-form-item>

            <el-form-item label="多维表格 App Token 白名单">
              <el-input
                v-model="form.feishu.allowedBitableTokens"
                type="textarea"
                :rows="2"
                placeholder="多个 Token 用英文逗号分隔，留空则 Agent 可访问任意可见多维表格"
              />
              <div class="form-hint">
                read_feishu_bitable 和 write_feishu_bitable 工具仅允许访问列出的 App Token。留空表示不限制。
              </div>
            </el-form-item>

            <el-form-item label="知识库 Space ID 白名单">
              <el-input
                v-model="form.feishu.allowedWikiSpaceIds"
                type="textarea"
                :rows="2"
                placeholder="多个 Space ID 用英文逗号分隔，留空则 Agent 可搜索任意可见知识库"
              />
              <div class="form-hint">
                search_feishu_wiki 工具仅允许搜索列出的知识库 Space ID。留空表示不限制。
              </div>
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
import { Plus, Edit, Delete, Cpu, Link, Connection, CopyDocument, Clock } from '@element-plus/icons-vue'
import {
  listChannels,
  createChannel,
  updateChannel,
  deleteChannel,
  testChannel,
  publishChannelMessage,
  getChannelStats,
  getChannelHealth,
  listProviders,
} from '@/services/gatewayApi'
import type { ChannelConfig, ChannelType, ProviderConfig, ChannelTestResult, ChannelStats, ChannelHealth } from '@/services/gatewayApi'

const loading = ref(false)
const submitting = ref(false)
const channels = ref<ChannelConfig[]>([])
const providers = ref<ProviderConfig[]>([])
const channelStats = reactive<Record<string, ChannelStats>>({})
const channelHealth = reactive<Record<string, ChannelHealth>>({})

// ── 健康仪表盘 Drawer ────────────────────────────────────────────────────────
const healthDrawerVisible = ref(false)
const healthDrawerChannel = ref<ChannelConfig | null>(null)

function openHealthDrawer(c: ChannelConfig) {
  healthDrawerChannel.value = c
  healthDrawerVisible.value = true
}

/** 根据 health 数据返回 ok / warn / error / unknown */
function healthLevel(id: string): 'ok' | 'warn' | 'error' | 'unknown' {
  const h = channelHealth[id]
  if (!h) return 'unknown'
  const status = h.connectionStatus
  if (status === 'disabled') return 'unknown'
  if (status === 'disconnected') return 'error'
  if (h.lastMessageSuccess === false) return 'warn'
  return 'ok'
}

function healthDotClass(id: string): string {
  const level = healthLevel(id)
  return `health-dot health-dot-${level}`
}

function formatRelativeTime(iso: string | null): string {
  if (!iso) return '暂无记录'
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60_000)
  if (mins < 1) return '刚刚'
  if (mins < 60) return `${mins} 分钟前`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return `${hrs} 小时前`
  return `${Math.floor(hrs / 24)} 天前`
}

function formatTokenTtl(secs: number | null): string {
  if (secs === null || secs === undefined) return '不适用'
  if (secs <= 0) return '已过期'
  const mins = Math.floor(secs / 60)
  if (mins < 60) return `${mins} 分钟`
  return `${Math.floor(mins / 60)} 小时 ${mins % 60} 分钟`
}

function connectionStatusLabel(status: string): { label: string; type: 'success' | 'danger' | 'info' | 'warning' } {
  switch (status) {
    case 'connected': return { label: '已连接', type: 'success' }
    case 'disconnected': return { label: '已断开', type: 'danger' }
    case 'webhook': return { label: 'Webhook 就绪', type: 'info' }
    case 'disabled': return { label: '已禁用', type: 'warning' }
    default: return { label: status, type: 'info' }
  }
}

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
    allowedDocTokens: '',
    allowedBitableTokens: '',
    allowedWikiSpaceIds: '',
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
    // F-F-3: 加载飞书渠道的错误事件统计数据
    // F-G-4: 同时加载健康数据
    const feishuChannels = channelList.filter(c => c.channelType === 'feishu')
    await Promise.allSettled([
      ...feishuChannels.map(c =>
        getChannelStats(c.id).then(stats => { channelStats[c.id] = stats }).catch(() => {})
      ),
      ...feishuChannels.map(c =>
        getChannelHealth(c.id).then(h => { channelHealth[c.id] = h }).catch(() => {})
      ),
    ])
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
    feishu: { appId: '', appSecret: '', encryptKey: '', verificationToken: '', connectionMode: 'websocket' as 'websocket' | 'webhook', apiBaseUrl: '', allowedDocTokens: '', allowedBitableTokens: '', allowedWikiSpaceIds: '' },
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
    feishu: { appId: '', appSecret: '', encryptKey: '', verificationToken: '', connectionMode: 'websocket' as 'websocket' | 'webhook', apiBaseUrl: '', allowedDocTokens: '', allowedBitableTokens: '', allowedWikiSpaceIds: '' },
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
      form.feishu.allowedDocTokens = (parsed.allowedDocTokens ?? []).join(', ')
      form.feishu.allowedBitableTokens = (parsed.allowedBitableTokens ?? []).join(', ')
      form.feishu.allowedWikiSpaceIds = (parsed.allowedWikiSpaceIds ?? []).join(', ')
    } catch { /* ignore */ }
  }

  dialogVisible.value = true
}

function buildSettingsJson(): string {
  if (form.channelType === 'feishu') {
    /** 将逗号分隔字符串拆分为清洁 token 数组，过滤空项 */
    const parseTokenList = (s: string): string[] =>
      s.split(',').map(t => t.trim()).filter(t => t.length > 0)

    return JSON.stringify({
      appId: form.feishu.appId,
      appSecret: form.feishu.appSecret || (isEditing.value ? '***' : ''),
      encryptKey: form.feishu.encryptKey,       // 如含 *** 后端自动保留原值
      verificationToken: form.feishu.verificationToken, // 同上
      connectionMode: form.feishu.connectionMode,
      apiBaseUrl: form.feishu.apiBaseUrl || undefined,
      allowedDocTokens: parseTokenList(form.feishu.allowedDocTokens),
      allowedBitableTokens: parseTokenList(form.feishu.allowedBitableTokens),
      allowedWikiSpaceIds: parseTokenList(form.feishu.allowedWikiSpaceIds),
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

async function refreshHealthDrawer() {
  if (!healthDrawerChannel.value) return
  const id = healthDrawerChannel.value.id
  await Promise.allSettled([
    getChannelStats(id).then(stats => { channelStats[id] = stats }).catch(() => {}),
    getChannelHealth(id).then(h => { channelHealth[id] = h }).catch(() => {}),
  ])
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

.card-connectivity-hint {
  margin-top: 6px;
  font-size: 12px;
}

/* F-F-3: 错误事件统计数字卡片 */
.card-stats {
  display: flex;
  gap: 8px;
  padding: 8px 0 0;
}

.stat-item {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 6px 4px;
  background: #f9fafb;
  border-radius: 6px;
  border: 1px solid #e5e7eb;
  min-width: 0;
}

.stat-item.stat-error {
  background: #fef2f2;
  border-color: #fecaca;
}

.stat-label {
  font-size: 11px;
  color: #9ca3af;
  white-space: nowrap;
}

.stat-item.stat-error .stat-label {
  color: #ef4444;
}

.stat-value {
  font-size: 18px;
  font-weight: 700;
  color: #374151;
  line-height: 1;
}

.stat-item.stat-error .stat-value {
  color: #dc2626;
}

.card-actions {
  display: flex;
  align-items: center;
}

/* F-G-4: 健康状态圆点 */
.health-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.health-dot-ok {
  background-color: #22c55e;
  box-shadow: 0 0 0 2px rgba(34, 197, 94, 0.25);
}

.health-dot-warn {
  background-color: #f59e0b;
  box-shadow: 0 0 0 2px rgba(245, 158, 11, 0.25);
}

.health-dot-error {
  background-color: #ef4444;
  box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.25);
}

.health-dot-unknown {
  background-color: #d1d5db;
}

/* F-G-4: 最近处理时间行 */
.card-last-message {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: #9ca3af;
}

.card-last-message .el-icon {
  flex-shrink: 0;
  font-size: 12px;
}

/* F-G-4: 健康 Drawer 内容 */
.health-drawer-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 4px 0;
}

.health-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.health-section-title {
  font-size: 13px;
  font-weight: 600;
  color: #374151;
  padding-bottom: 6px;
  border-bottom: 1px solid #f3f4f6;
}

.health-rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.health-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.health-row-col {
  flex-direction: column;
  align-items: flex-start;
}

.health-label {
  font-size: 13px;
  color: #6b7280;
  flex-shrink: 0;
}

.health-value {
  font-size: 13px;
  color: #111827;
  text-align: right;
  word-break: break-all;
}

.health-mono {
  font-family: 'SF Mono', 'Fira Code', monospace;
  font-size: 12px;
}

.health-error-text {
  color: #dc2626;
  font-size: 12px;
}

.health-empty {
  font-size: 13px;
  color: #9ca3af;
  padding: 8px 0;
}

.health-stat-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
}

.health-stat-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 12px 8px;
  background: #f9fafb;
  border-radius: 8px;
  border: 1px solid #e5e7eb;
}

.health-stat-card.health-stat-error {
  background: #fef2f2;
  border-color: #fecaca;
}

.health-stat-num {
  font-size: 24px;
  font-weight: 700;
  color: #374151;
  line-height: 1;
}

.health-stat-card.health-stat-error .health-stat-num {
  color: #dc2626;
}

.health-stat-desc {
  font-size: 11px;
  color: #9ca3af;
  text-align: center;
}

.health-stat-card.health-stat-error .health-stat-desc {
  color: #ef4444;
}

.health-drawer-footer {
  padding-top: 8px;
  display: flex;
  justify-content: flex-end;
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

/* F-G-3: 云文档工具配置区块 */
.section-header-doc {
  background: #fffbeb;
  color: #b45309;
}

.section-header-doc .section-subtitle {
  color: #fcd34d;
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
