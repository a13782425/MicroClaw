<template>
  <div class="dashboard">
    <div class="page-header">
      <h2 class="page-title">控制台</h2>
      <p class="page-desc">欢迎使用 MicroClaw AI Agent 控制面板</p>
    </div>

    <el-row :gutter="20">
      <el-col v-for="item in quickLinks" :key="item.path" :xs="24" :sm="12" :md="8" :lg="6">
        <el-card
          class="quick-card"
          shadow="hover"
          @click="router.push(item.path)"
        >
          <div class="quick-card-inner">
            <el-icon class="quick-icon" :style="{ color: item.color }">
              <component :is="item.icon" />
            </el-icon>
            <div class="quick-info">
              <div class="quick-title">{{ item.title }}</div>
              <div class="quick-desc">{{ item.desc }}</div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { ChatDotRound, Timer, Cpu, Setting } from '@element-plus/icons-vue'

const router = useRouter()

const quickLinks = [
  {
    path: '/sessions',
    title: '会话',
    desc: '管理 AI Agent 对话会话',
    icon: ChatDotRound,
    color: '#3b82f6',
  },
  {
    path: '/cron',
    title: '计时任务',
    desc: '配置与管理定时调度任务',
    icon: Timer,
    color: '#8b5cf6',
  },
  {
    path: '/models',
    title: '模型',
    desc: '查看与管理模型提供方',
    icon: Cpu,
    color: '#10b981',
  },
  {
    path: '/config',
    title: '配置',
    desc: '系统渠道与全局配置管理',
    icon: Setting,
    color: '#f59e0b',
  },
]
</script>

<style scoped>
.dashboard {
  max-width: 1200px;
  height: 100%;
  padding: 24px;
  overflow-y: auto;
  box-sizing: border-box;
}

.page-header {
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

.quick-card {
  border-radius: 10px;
  cursor: pointer;
  transition: transform 0.15s ease;
  margin-bottom: 20px;
}

.quick-card:hover {
  transform: translateY(-2px);
}

.quick-card-inner {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 4px 0;
}

.quick-icon {
  font-size: 32px;
  flex-shrink: 0;
}

.quick-title {
  font-size: 15px;
  font-weight: 600;
  color: #1f2937;
  margin-bottom: 4px;
}

.quick-desc {
  font-size: 13px;
  color: #6b7280;
}
</style>
