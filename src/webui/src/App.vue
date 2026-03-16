<template>
  <template v-if="!auth.isLoggedIn">
    <RouterView />
  </template>

  <el-container v-else class="app-layout">
    <el-header class="app-header">
      <div class="header-brand">
        <el-icon class="brand-icon"><Cpu /></el-icon>
        <span class="brand-name">MicroClaw</span>
      </div>
      <el-menu
        mode="horizontal"
        :ellipsis="false"
        :router="true"
        :default-active="route.path"
        background-color="transparent"
        text-color="rgba(255,255,255,0.85)"
        active-text-color="#ffffff"
        class="header-nav"
      >
        <el-menu-item index="/">
          <el-icon><Monitor /></el-icon>控制台
        </el-menu-item>
      </el-menu>

      <div class="header-user">
        <el-icon><User /></el-icon>
        <span class="username">{{ auth.username }}</span>
        <el-divider direction="vertical" />
        <el-button link type="primary" @click="logout">
          <el-icon><SwitchButton /></el-icon>退出
        </el-button>
      </div>
    </el-header>

    <el-main class="app-main">
      <RouterView />
    </el-main>
  </el-container>
</template>

<script setup lang="ts">
import { useRoute, useRouter, RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

function logout() {
  auth.clearAuth()
  router.push({ name: 'login' })
}
</script>

<style>
html, body, #app {
  height: 100%;
  margin: 0;
}

.app-layout {
  min-height: 100vh;
  background: #f0f2f5;
}

.app-header {
  display: flex;
  align-items: center;
  background: #1d2b45;
  padding: 0 24px;
  height: 60px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.header-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #fff;
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.5px;
  margin-right: 24px;
  white-space: nowrap;
}

.brand-icon {
  font-size: 22px;
  color: #60a5fa;
}

.header-nav {
  flex: 1;
  border-bottom: none !important;
}

.header-nav .el-menu-item {
  height: 60px;
  line-height: 60px;
  border-bottom: none !important;
}

.header-nav .el-menu-item.is-active {
  background: rgba(255, 255, 255, 0.1) !important;
  border-bottom: 2px solid #60a5fa !important;
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

.header-user .el-divider {
  background-color: rgba(255, 255, 255, 0.2);
}

.app-main {
  padding: 24px;
}
</style>
