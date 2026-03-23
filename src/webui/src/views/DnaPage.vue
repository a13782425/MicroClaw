<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">DNA 管理</h2>
    </div>
    <el-result
      icon="info"
      title="DNA 管理已移至会话管理"
      sub-title="Session DNA（SOUL / USER / AGENTS）现在直接在「会话管理」页面的 DNA 标签页中编辑，请前往会话管理页面进行操作。"
    >
      <template #extra>
        <el-button type="primary" @click="$router.push('/sessions')">前往会话管理</el-button>
      </template>
    </el-result>
  </div>
</template>

<script setup lang="ts">
</script>

<style scoped>
.page-container {
  padding: 24px;
  max-width: 900px;
}

.page-header {
  margin-bottom: 16px;
}

.page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}
</style>

