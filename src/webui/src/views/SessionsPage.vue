<template>
  <div class="sessions-layout">
    <!-- 左侧：会话列表 -->
    <div class="sidebar">
      <div class="sidebar-header">
        <span class="sidebar-title">会话</span>
        <el-button type="primary" :icon="Plus" circle size="small" @click="showCreate = true" />
      </div>

      <div class="session-list">
        <div
          v-for="session in store.sessions"
          :key="session.id"
          class="session-item"
          :class="{ active: store.currentSessionId === session.id }"
          @click="handleSelect(session.id)"
        >
          <div class="session-info">
            <el-icon class="session-icon" :size="14">
              <ChatDotRound />
            </el-icon>
            <span class="session-title">{{ session.title }}</span>
            <el-tag
              v-if="!isWebChannel(session.channelType)"
              size="small"
              type="info"
              effect="plain"
              class="channel-tag"
            >{{ channelLabel(session.channelType) }}</el-tag>
          </div>
          <div class="session-actions">
            <el-button
              link
              type="danger"
              size="small"
              :icon="Delete"
              title="删除会话"
              @click.stop="handleDelete(session.id)"
            />
          </div>
        </div>

        <div v-if="store.sessions.length === 0" class="empty-sessions">
          <el-empty description="暂无会话" :image-size="60" />
        </div>
      </div>
    </div>

    <!-- 右侧：聊天区 -->
    <div class="chat-area">
      <!-- 无会话选中 -->
      <div v-if="!store.currentSessionId" class="chat-placeholder">
        <el-empty description="选择或创建一个会话开始对话" :image-size="100">
          <el-button type="primary" @click="showCreate = true">新建会话</el-button>
        </el-empty>
      </div>

      <template v-else>
        <!-- 聊天头部 -->
        <div class="chat-header">
          <div class="chat-title">
            <el-icon><ChatDotRound /></el-icon>
            <span>{{ store.currentSession()?.title }}</span>
          </div>
        </div>

        <!-- 消息列表 -->
        <div class="chat-messages" ref="messagesEl">
          <div v-if="store.loading" class="loading-wrap">
            <el-skeleton :rows="4" animated />
          </div>
          <template v-else>
            <ChatMessage
              v-for="(msg, idx) in store.messages"
              :key="idx"
              :msg="msg"
              :is-streaming="store.chatting && idx === store.messages.length - 1"
            />
          </template>
        </div>

        <!-- 输入区 -->
        <div class="input-area">
          <!-- 附件预览 -->
          <div v-if="pendingAttachments.length > 0" class="pending-attachments">
            <div
              v-for="(att, i) in pendingAttachments"
              :key="i"
              class="pending-att-item"
            >
              <img
                v-if="att.mimeType.startsWith('image/')"
                :src="`data:${att.mimeType};base64,${att.base64Data}`"
                class="pending-att-thumb"
              />
              <span v-else class="pending-att-name">{{ att.fileName }}</span>
              <el-button link :icon="Close" size="small" @click="removeAttachment(i)" />
            </div>
          </div>

          <div class="input-row">
            <!-- 上传按钮 -->
            <el-upload
              ref="uploadRef"
              action="#"
              :auto-upload="false"
              :show-file-list="false"
              multiple
              accept="image/*,text/*,.pdf,.doc,.docx"
              :on-change="handleFileChange"
            >
              <el-button :icon="Paperclip" circle size="default" title="上传文件/图片" />
            </el-upload>

            <el-input
              v-model="inputText"
              type="textarea"
              :autosize="{ minRows: 1 }"
              placeholder="输入消息，Enter 发送，Shift+Enter 换行..."
              resize="none"
              :disabled="store.chatting"
              @keydown="handleKeydown"
            />

            <el-button
              v-if="store.chatting"
              type="danger"
              :icon="VideoPause"
              circle
              size="default"
              title="停止生成"
              @click="store.abortChat()"
            />
            <el-button
              v-else
              type="primary"
              :icon="Promotion"
              circle
              size="default"
              :disabled="!inputText.trim() && pendingAttachments.length === 0"
              title="发送（Enter）"
              @click="handleSend"
            />
          </div>
        </div>
      </template>
    </div>

    <!-- 新建会话对话框 -->
    <el-dialog v-model="showCreate" title="新建会话" width="420px" :close-on-click-modal="false">
      <el-form :model="createForm" label-width="80px">
        <el-form-item label="会话名称" required>
          <el-input v-model="createForm.title" placeholder="为此会话取个名字" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item label="使用模型" required>
          <el-select v-model="createForm.providerId" placeholder="选择 AI 模型" style="width: 100%">
            <el-option
              v-for="p in enabledProviders"
              :key="p.id"
              :label="p.displayName + ' (' + p.modelName + ')'"
              :value="p.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showCreate = false">取消</el-button>
        <el-button
          type="primary"
          :loading="creating"
          :disabled="!createForm.title.trim() || !createForm.providerId"
          @click="handleCreate"
        >创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue'
import { useSessionStore } from '@/stores/sessionStore'
import { listProviders, type ProviderConfig, type MessageAttachment } from '@/services/gatewayApi'
import ChatMessage from '@/components/ChatMessage.vue'
import {
  Plus, Delete, ChatDotRound, Paperclip,
  Promotion, VideoPause, Close,
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const store = useSessionStore()

const showCreate = ref(false)
const creating = ref(false)
const inputText = ref('')
const pendingAttachments = ref<MessageAttachment[]>([])
const messagesEl = ref<HTMLElement | null>(null)
const providers = ref<ProviderConfig[]>([])
const enabledProviders = ref<ProviderConfig[]>([])

const createForm = ref({ title: '', providerId: '' })

onMounted(async () => {
  await store.fetchSessions()
  providers.value = await listProviders()
  enabledProviders.value = providers.value.filter((p) => p.isEnabled)
})

// 滚动到底部
function scrollToBottom() {
  nextTick(() => {
    if (messagesEl.value) {
      messagesEl.value.scrollTop = messagesEl.value.scrollHeight
    }
  })
}

watch(() => store.messages.length, scrollToBottom)
watch(() => store.chatting, scrollToBottom)

async function handleSelect(id: string) {
  await store.selectSession(id)
  scrollToBottom()
}

async function handleCreate() {
  if (!createForm.value.title.trim() || !createForm.value.providerId) return
  creating.value = true
  try {
    const session = await store.addSession({
      title: createForm.value.title.trim(),
      providerId: createForm.value.providerId
    })
    showCreate.value = false
    createForm.value = { title: '', providerId: '' }
    await store.selectSession(session.id)
    ElMessage.success('会话已创建，请等待管理员批准后方可对话')
  } catch {
    ElMessage.error('创建失败，请重试')
  } finally {
    creating.value = false
  }
}

async function handleDelete(id: string) {
  await ElMessageBox.confirm('确定删除此会话及所有消息记录？', '删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
    confirmButtonClass: 'el-button--danger'
  })
  await store.removeSession(id)
  ElMessage.success('已删除')
}

const channelLabelMap: Record<string, string> = {
  web: 'Web',
  feishu: '飞书',
  wecom: '企微',
  wechat: '微信',
}

function channelLabel(type: string): string {
  return channelLabelMap[type] ?? type
}

function isWebChannel(type: string): boolean {
  return !type || type === 'web'
}

async function handleSend() {
  const text = inputText.value.trim()
  if (!text && pendingAttachments.value.length === 0) return
  if (store.chatting) return

  const atts = [...pendingAttachments.value]
  inputText.value = ''
  pendingAttachments.value = []

  await store.sendMessage(text, atts.length > 0 ? atts : undefined)
  scrollToBottom()
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
}

async function handleFileChange(file: { raw: File }) {
  const raw = file.raw
  const reader = new FileReader()
  reader.onload = (ev) => {
    const dataUrl = ev.target?.result as string
    const base64 = dataUrl.split(',')[1]
    pendingAttachments.value.push({
      fileName: raw.name,
      mimeType: raw.type || 'application/octet-stream',
      base64Data: base64
    })
  }
  reader.readAsDataURL(raw)
}

function removeAttachment(idx: number) {
  pendingAttachments.value.splice(idx, 1)
}
</script>

<style scoped>
.sessions-layout {
  display: flex;
  height: 100%;
  background: var(--el-bg-color);
  overflow: hidden;
}

/* ── 左侧边栏 ── */
.sidebar {
  width: 240px;
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
  color: var(--el-text-color-primary);
}

.session-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.session-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
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
  color: var(--el-color-primary);
}

.session-info {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
  flex: 1;
}

.session-icon {
  flex-shrink: 0;
  color: var(--el-text-color-secondary);
}

.session-title {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.channel-tag {
  flex-shrink: 0;
  font-size: 10px;
  padding: 0 4px;
}

.session-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 0.15s;
}

.session-item:hover .session-actions {
  opacity: 1;
}

.empty-sessions {
  padding: 24px 0;
  display: flex;
  justify-content: center;
}

/* ── 右侧聊天区 ── */
.chat-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}

.chat-placeholder {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.chat-header {
  padding: 12px 20px;
  border-bottom: 1px solid var(--el-border-color-light);
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: var(--el-fill-color-blank);
  flex-shrink: 0;
}

.chat-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 15px;
}

/* 消息列表 */
.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.loading-wrap {
  padding: 20px;
}

/* 输入区 */
.input-area {
  border-top: 1px solid var(--el-border-color-light);
  padding: 12px 16px;
  background: var(--el-fill-color-blank);
  flex-shrink: 0;
  max-height: 40vh;
  overflow-y: auto;
}

.pending-attachments {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 8px;
}

.pending-att-item {
  display: flex;
  align-items: center;
  gap: 4px;
  background: var(--el-fill-color);
  border-radius: 6px;
  padding: 4px 6px;
}

.pending-att-thumb {
  width: 36px;
  height: 36px;
  border-radius: 4px;
  object-fit: cover;
}

.pending-att-name {
  font-size: 12px;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.input-row {
  display: flex;
  align-items: flex-end;
  gap: 10px;
}

.input-row .el-textarea {
  flex: 1;
}
</style>
