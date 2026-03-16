<template>
  <div class="dashboard">
    <div class="page-header">
      <h2 class="page-title">控制台</h2>
      <p class="page-desc">查看网关运行状态与系统信息</p>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="24" :md="12" :lg="8">
        <el-card class="status-card" shadow="hover">
          <template #header>
            <div class="card-header">
              <el-icon class="card-icon status-icon"><Connection /></el-icon>
              <span>Gateway 状态</span>
              <el-button
                :icon="Refresh"
                circle
                size="small"
                :loading="loading"
                class="refresh-btn"
                @click="load"
              />
            </div>
          </template>

          <div v-if="loading" class="status-loading">
            <el-skeleton :rows="4" animated />
          </div>

          <el-alert
            v-else-if="error"
            :title="error"
            type="error"
            :closable="false"
            show-icon
          />

          <template v-else-if="health">
            <el-descriptions :column="1" border size="small">
              <el-descriptions-item label="运行状态">
                <el-tag :type="health.status === 'ok' ? 'success' : 'danger'" effect="light">
                  <el-icon v-if="health.status === 'ok'"><CircleCheckFilled /></el-icon>
                  {{ health.status }}
                </el-tag>
              </el-descriptions-item>
              <el-descriptions-item label="服务名称">{{ health.service }}</el-descriptions-item>
              <el-descriptions-item label="版本">{{ health.version }}</el-descriptions-item>
              <el-descriptions-item label="服务时间">{{ formatTime(health.utcNow) }}</el-descriptions-item>
            </el-descriptions>
          </template>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Refresh, Connection, CircleCheckFilled } from '@element-plus/icons-vue'
import { getGatewayHealth, type GatewayHealth } from '@/services/gatewayApi'

const loading = ref(false)
const error = ref('')
const health = ref<GatewayHealth | null>(null)

function formatTime(iso: string) {
  return new Date(iso).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai' })
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    health.value = await getGatewayHealth()
  } catch {
    error.value = '无法连接到网关，请检查服务是否正常运行'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.dashboard {
  max-width: 1200px;
}

.page-header {
  margin-bottom: 24px;
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

.status-card {
  border-radius: 10px;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}

.card-icon {
  font-size: 18px;
}

.status-icon {
  color: #3b82f6;
}

.refresh-btn {
  margin-left: auto;
}

.status-loading {
  padding: 8px 0;
}
</style>
