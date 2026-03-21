<template>
  <el-config-provider :locale="zhCn">
  <template v-if="!auth.isLoggedIn">
    <RouterView />
  </template>

  <el-container v-else class="app-layout">
    <el-header class="app-header">
      <div class="header-brand">
        <el-icon class="brand-icon"><Cpu /></el-icon>
        <span class="brand-name">MicroClaw</span>
      </div>

      <div class="header-right">
        <div class="gateway-status" :title="gatewayError || '网关连接正常'">
          <span class="status-dot" :class="gatewayOk ? 'ok' : 'err'"></span>
          <span class="status-label">{{ gatewayOk ? 'ok' : 'err' }}</span>
          <span v-if="gatewayVersion" class="status-version">v{{ gatewayVersion }}</span>
        </div>

        <el-divider direction="vertical" />

        <div class="header-user">
          <el-icon><User /></el-icon>
          <span class="username">{{ auth.username }}</span>
          <el-divider direction="vertical" />
          <el-button link type="primary" @click="logout">
            <el-icon><SwitchButton /></el-icon>退出
          </el-button>
        </div>
      </div>
    </el-header>

    <el-container class="app-body">
      <el-aside :width="isSidebarCollapsed ? '64px' : '200px'" class="app-aside">
        <el-menu
          :router="true"
          :default-active="route.path"
          :collapse="isSidebarCollapsed"
          :collapse-transition="false"
          class="aside-menu"
        >
          <el-sub-menu v-for="group in menuGroups" :key="group.id" :index="group.id">
            <template #title>
              <el-icon><component :is="group.icon" /></el-icon>
              <span>{{ group.label }}</span>
            </template>
            <el-menu-item
              v-for="item in group.items"
              :key="item.route"
              :index="item.route"
            >
              <el-icon><component :is="item.icon" /></el-icon>
              <span>{{ item.label }}</span>
            </el-menu-item>
          </el-sub-menu>
        </el-menu>

        <div class="sidebar-toggle" @click="isSidebarCollapsed = !isSidebarCollapsed">
          <el-icon><ArrowLeft v-if="!isSidebarCollapsed" /><ArrowRight v-else /></el-icon>
        </div>
      </el-aside>

      <el-main class="app-main">
        <RouterView />
      </el-main>
    </el-container>
  </el-container>
  </el-config-provider>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, h } from 'vue'
import { useRoute, useRouter, RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { getGatewayHealth } from '@/services/gatewayApi'
import { menuGroups } from '@/config/menu'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { ElNotification, ElButton } from 'element-plus'
import { eventBus } from '@/services/eventBus'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const isSidebarCollapsed = ref(false)
const gatewayOk = ref(false)
const gatewayVersion = ref('')
const gatewayError = ref('')
let pollTimer: ReturnType<typeof setInterval> | null = null
let hubConnection: HubConnection | null = null

async function fetchGatewayStatus() {
  try {
    const health = await getGatewayHealth()
    gatewayOk.value = health.status === 'ok'
    gatewayVersion.value = health.version
    gatewayError.value = ''
  } catch {
    gatewayOk.value = false
    gatewayError.value = '无法连接到网关'
  }
}

function logout() {
  stopSignalR()
  auth.clearAuth()
  router.push({ name: 'login' })
}

function startSignalR() {
  if (hubConnection) return
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/gateway', { accessTokenFactory: () => auth.token ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('sessionPendingApproval', (payload: { sessionId: string; sessionTitle: string; channelType: string; timestamp: string }) => {
    eventBus.emit('session:pendingApproval', payload)
    ElNotification({
      title: '新会话待审批',
      message: h('div', [
        h('p', { style: 'margin:0 0 8px' }, `来自 ${payload.channelType} 的会话「${payload.sessionTitle}」需要审批`),
        h(ElButton, {
          type: 'primary',
          size: 'small',
          onClick: () => {
            router.push('/approvals')
          }
        }, () => '前往审批')
      ]),
      type: 'warning',
      duration: 10000,
      position: 'bottom-right'
    })
  })

  connection.on('sessionCreated', (payload: { sessionId: string; title: string; channelType: string }) => {
    eventBus.emit('session:created', payload)
  })

  connection.on('sessionApproved', (payload: { sessionId: string; title: string }) => {
    eventBus.emit('session:approved', payload)
  })

  connection.on('sessionDisabled', (payload: { sessionId: string; title: string }) => {
    eventBus.emit('session:disabled', payload)
  })

  connection.on('cronJobExecuted', (payload: { sessionId: string; content: string }) => {
    eventBus.emit('cron:jobExecuted', payload)
    ElNotification({
      title: '定时任务已执行',
      message: payload.content.length > 80 ? payload.content.slice(0, 80) + '…' : payload.content,
      type: 'info',
      duration: 8000,
      position: 'bottom-right'
    })
  })

  connection.on('agentStatus', (payload: { sessionId: string; agentId: string; status: 'running' | 'completed' | 'failed' }) => {
    eventBus.emit('agent:statusChanged', payload)
  })

  connection.start().catch(() => {
    // 连接失败时静默，自动重连会处理
  })
  hubConnection = connection
}

function stopSignalR() {
  if (hubConnection) {
    hubConnection.stop()
    hubConnection = null
  }
}

watch(() => auth.isLoggedIn, (loggedIn) => {
  if (loggedIn) {
    startSignalR()
  } else {
    stopSignalR()
  }
})

onMounted(() => {
  if (auth.isLoggedIn) {
    fetchGatewayStatus()
    pollTimer = setInterval(fetchGatewayStatus, 30000)
    startSignalR()
  }
})

onUnmounted(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
  }
  stopSignalR()
})
</script>

<style>
html, body, #app {
  height: 100%;
  margin: 0;
}

.app-layout {
  height: 100vh;
  background: #f0f2f5;
  flex-direction: column;
}

.app-header {
  display: flex;
  align-items: center;
  background: #1d2b45;
  padding: 0 24px;
  height: 56px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  flex-shrink: 0;
}

.header-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #fff;
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.5px;
  white-space: nowrap;
}

.brand-icon {
  font-size: 22px;
  color: #60a5fa;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
}

.gateway-status {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 13px;
  color: rgba(255, 255, 255, 0.8);
  cursor: default;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-dot.ok {
  background: #22c55e;
  box-shadow: 0 0 4px rgba(34, 197, 94, 0.6);
}

.status-dot.err {
  background: #ef4444;
  box-shadow: 0 0 4px rgba(239, 68, 68, 0.6);
}

.status-label {
  font-size: 12px;
  font-weight: 500;
}

.status-version {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.5);
  margin-left: 2px;
}

.header-user {
  display: flex;
  align-items: center;
  gap: 8px;
  color: rgba(255, 255, 255, 0.85);
  font-size: 14px;
  white-space: nowrap;
}

.header-user .el-button {
  color: rgba(255, 255, 255, 0.75) !important;
  font-size: 14px;
}

.header-user .el-button:hover {
  color: #fff !important;
}

.header-user .el-divider,
.header-right .el-divider {
  background-color: rgba(255, 255, 255, 0.2);
}

.app-body {
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.app-aside {
  background: #fff;
  border-right: 1px solid #e5e7eb;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  transition: width 0.25s ease;
  overflow-x: hidden;
}

.aside-menu {
  border-right: none !important;
  flex: 1;
  padding-top: 8px;
}

.aside-menu .el-menu-item {
  height: 44px;
  line-height: 44px;
  font-size: 14px;
  border-radius: 6px;
  margin: 2px 8px;
  width: calc(100% - 16px);
}

.aside-menu .el-menu-item.is-active {
  background: #eff6ff !important;
  color: #2563eb !important;
}

.aside-menu .el-menu-item:hover {
  background: #f3f4f6 !important;
}

.aside-menu .el-sub-menu__title {
  height: 44px;
  line-height: 44px;
  font-size: 13px;
  font-weight: 600;
  color: #6b7280 !important;
  letter-spacing: 0.3px;
  border-radius: 6px;
  margin: 2px 8px;
  width: calc(100% - 16px);
  padding-right: 8px !important;
  text-transform: uppercase;
}

.aside-menu .el-sub-menu__title:hover {
  background: #f3f4f6 !important;
  color: #374151 !important;
}

.aside-menu .el-sub-menu__title .el-sub-menu__icon-arrow {
  font-size: 11px;
  color: #9ca3af;
  transition: transform 0.2s ease, color 0.2s ease;
  right: 10px;
}

.aside-menu .el-sub-menu.is-opened > .el-sub-menu__title {
  color: #1d4ed8 !important;
}

.aside-menu .el-sub-menu.is-opened > .el-sub-menu__title .el-icon:first-child {
  color: #2563eb;
}

.aside-menu .el-sub-menu.is-opened > .el-sub-menu__title .el-sub-menu__icon-arrow {
  color: #2563eb;
}

.sidebar-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 44px;
  cursor: pointer;
  border-top: 1px solid #e5e7eb;
  color: #6b7280;
  font-size: 16px;
  flex-shrink: 0;
  transition: background 0.15s;
}

.sidebar-toggle:hover {
  background: #f3f4f6;
  color: #2563eb;
}

.app-main {
  padding: 0;
  overflow: hidden;
}
</style>
