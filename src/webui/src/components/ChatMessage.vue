<template>
  <div class="chat-message" :class="msg.role">
    <!-- 头像 -->
    <div class="avatar">
      <el-icon v-if="msg.role === 'user'" :size="20"><User /></el-icon>
      <el-icon v-else :size="20"><Cpu /></el-icon>
    </div>

    <div class="bubble-wrapper">
      <!-- Think 块（可折叠） -->
      <div v-if="msg.thinkContent" class="think-block">
        <div class="think-header" @click="thinkOpen = !thinkOpen">
          <el-icon :size="14"><Loading v-if="isStreaming && !msg.thinkContent" /><Memo v-else /></el-icon>
          <span>思考过程</span>
          <el-icon :size="12" class="chevron" :class="{ open: thinkOpen }"><ArrowDown /></el-icon>
        </div>
        <div v-show="thinkOpen" class="think-content" v-html="renderMd(msg.thinkContent)" />
      </div>

      <!-- 用户消息：纯文字，显示附件 -->
      <template v-if="msg.role === 'user'">
        <div class="bubble user-bubble">
          <div class="message-text">{{ msg.content }}</div>
          <!-- 附件预览 -->
          <div v-if="msg.attachments && msg.attachments.length > 0" class="attachments">
            <template v-for="att in msg.attachments" :key="att.fileName">
              <div v-if="att.mimeType.startsWith('image/')" class="attachment-img">
                <img :src="`data:${att.mimeType};base64,${att.base64Data}`" :alt="att.fileName" />
              </div>
              <div v-else class="attachment-file">
                <el-icon><Document /></el-icon>
                <span>{{ att.fileName }}</span>
              </div>
            </template>
          </div>
        </div>
      </template>

      <!-- AI 消息：Markdown 渲染 -->
      <template v-else>
        <div class="bubble assistant-bubble">
          <div
            v-if="msg.content"
            class="markdown-body"
            v-html="renderedContent"
          />
          <span v-else class="typing-cursor">▍</span>
        </div>
      </template>

      <div class="timestamp">{{ formatTime(msg.timestamp) }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { marked, Renderer } from 'marked'
import hljs from 'highlight.js'
import DOMPurify from 'dompurify'
import mermaid from 'mermaid'
import type { SessionMessage } from '@/stores/sessionStore'
import { User, Cpu, Memo, ArrowDown, Document, Loading } from '@element-plus/icons-vue'

const props = defineProps<{
  msg: SessionMessage
  isStreaming?: boolean
}>()

const thinkOpen = ref(false)
let mermaidCounter = 0

mermaid.initialize({
  startOnLoad: false,
  theme: 'neutral',
  securityLevel: 'loose'
})

// 配置 marked：代码高亮 + mermaid 特殊处理
const renderer = new Renderer()

renderer.code = ({ text, lang }: { text: string; lang?: string }) => {
  const language = lang ?? ''
  if (language === 'mermaid') {
    const id = `mermaid-${Date.now()}-${mermaidCounter++}`
    return `<div class="mermaid-block" data-mermaid="${encodeURIComponent(text)}" data-id="${id}"><div id="${id}">${text}</div></div>`
  }
  const validLang = hljs.getLanguage(language) ? language : 'plaintext'
  const highlighted = hljs.highlight(text, { language: validLang }).value
  return `<pre><code class="hljs language-${validLang}">${highlighted}</code></pre>`
}

marked.use({ renderer })

function renderMd(content: string): string {
  const raw = marked.parse(content) as string
  return DOMPurify.sanitize(raw, { ADD_ATTR: ['data-mermaid', 'data-id'] })
}

const renderedContent = computed(() => renderMd(props.msg.content))

// 渲染 mermaid 图表
async function renderMermaidBlocks(el: Element) {
  const blocks = el.querySelectorAll<HTMLElement>('.mermaid-block')
  for (const block of blocks) {
    const encoded = block.getAttribute('data-mermaid')
    const id = block.getAttribute('data-id')
    if (!encoded || !id) continue
    const code = decodeURIComponent(encoded)
    const target = block.querySelector(`#${id}`)
    if (!target) continue
    try {
      const { svg } = await mermaid.render(id + '-svg', code)
      target.innerHTML = svg
    } catch {
      target.textContent = code
    }
  }
}

// 每次内容变化后渲染 mermaid
watch(renderedContent, async () => {
  await nextTick()
  const el = document.querySelector('.chat-messages')
  if (el) renderMermaidBlocks(el)
})

onMounted(async () => {
  await nextTick()
  const el = document.querySelector('.chat-messages')
  if (el) renderMermaidBlocks(el)
})

function formatTime(ts: string): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}
</script>

<style scoped>
.chat-message {
  display: flex;
  gap: 10px;
  padding: 8px 0;
}

.chat-message.user {
  flex-direction: row-reverse;
}

.avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--el-fill-color);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-top: 2px;
}

.chat-message.user .avatar {
  background: var(--el-color-primary-light-7);
}

.bubble-wrapper {
  max-width: 75%;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.chat-message.user .bubble-wrapper {
  align-items: flex-end;
}

.bubble {
  padding: 10px 14px;
  border-radius: 12px;
  line-height: 1.6;
  word-break: break-word;
}

.user-bubble {
  background: var(--el-color-primary);
  color: #fff;
  border-bottom-right-radius: 4px;
}

.user-bubble .message-text {
  white-space: pre-wrap;
}

.assistant-bubble {
  background: var(--el-fill-color-light);
  border-bottom-left-radius: 4px;
  min-width: 40px;
}

.typing-cursor {
  display: inline-block;
  animation: blink 1s step-end infinite;
  color: var(--el-color-primary);
  font-size: 18px;
  line-height: 1;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0; }
}

.timestamp {
  font-size: 11px;
  color: var(--el-text-color-placeholder);
  padding: 0 2px;
}

/* Think 块 */
.think-block {
  background: var(--el-fill-color-lighter);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  overflow: hidden;
  font-size: 13px;
}

.think-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  cursor: pointer;
  user-select: none;
  color: var(--el-text-color-secondary);
  font-weight: 500;
}

.think-header:hover {
  background: var(--el-fill-color);
}

.chevron {
  margin-left: auto;
  transition: transform 0.2s;
}

.chevron.open {
  transform: rotate(180deg);
}

.think-content {
  padding: 0 12px 10px;
  color: var(--el-text-color-secondary);
  border-top: 1px solid var(--el-border-color-lighter);
  font-size: 13px;
}

/* 附件 */
.attachments {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.attachment-img img {
  max-width: 200px;
  max-height: 200px;
  border-radius: 6px;
  object-fit: cover;
}

.attachment-file {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  background: rgba(255,255,255,0.2);
  border-radius: 4px;
  font-size: 13px;
}

/* Markdown 样式 */
.markdown-body :deep(p) {
  margin: 4px 0 8px;
}

.markdown-body :deep(h1),
.markdown-body :deep(h2),
.markdown-body :deep(h3) {
  margin: 12px 0 6px;
  font-weight: 600;
}

.markdown-body :deep(code:not(pre > code)) {
  background: var(--el-fill-color);
  padding: 1px 5px;
  border-radius: 4px;
  font-family: 'Courier New', monospace;
  font-size: 0.9em;
}

.markdown-body :deep(pre) {
  background: #1e1e1e;
  border-radius: 8px;
  padding: 12px 16px;
  overflow-x: auto;
  margin: 8px 0;
}

.markdown-body :deep(pre code) {
  background: transparent;
  padding: 0;
  color: #d4d4d4;
  font-size: 13px;
  font-family: 'Courier New', Consolas, monospace;
}

.markdown-body :deep(blockquote) {
  border-left: 3px solid var(--el-color-primary);
  margin: 8px 0;
  padding: 4px 12px;
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-lighter);
  border-radius: 0 4px 4px 0;
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  padding-left: 20px;
  margin: 4px 0 8px;
}

.markdown-body :deep(table) {
  border-collapse: collapse;
  width: 100%;
  margin: 8px 0;
  font-size: 13px;
}

.markdown-body :deep(th),
.markdown-body :deep(td) {
  border: 1px solid var(--el-border-color);
  padding: 6px 10px;
  text-align: left;
}

.markdown-body :deep(th) {
  background: var(--el-fill-color);
  font-weight: 600;
}

.markdown-body :deep(.mermaid-block) {
  margin: 8px 0;
  overflow-x: auto;
}

.markdown-body :deep(.mermaid-block svg) {
  max-width: 100%;
}
</style>
