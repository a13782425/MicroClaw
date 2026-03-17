<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">计时任务</h2>
      <p class="page-desc">配置与管理定时调度任务</p>
    </div>
    <el-empty description="功能开发中，敬请期待" :image-size="120">
      <template #image>
        <el-icon class="placeholder-icon"><Timer /></el-icon>
      </template>
    </el-empty>
  </div>
</template>

<script setup lang="ts">
import { Timer } from '@element-plus/icons-vue'
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
  margin-bottom: 32px;
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

.placeholder-icon {
  font-size: 80px;
  color: #d1d5db;
}
</style>
