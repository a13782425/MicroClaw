<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">RAG 知识库</h2>
      <el-tag type="info" class="wip-badge">开发中</el-tag>
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="全局知识库" name="global">
        <el-result
          icon="info"
          title="全局 RAG 知识库"
          sub-title="全局知识库功能正在开发中，将支持所有会话共享的向量知识库。"
        >
          <template #extra>
            <div class="feature-list">
              <el-tag type="info" effect="plain">向量存储（规划中）</el-tag>
              <el-tag type="info" effect="plain">文档导入</el-tag>
              <el-tag type="info" effect="plain">语义搜索</el-tag>
            </div>
          </template>
        </el-result>
      </el-tab-pane>

      <el-tab-pane label="会话知识库" name="session">
        <el-result
          icon="info"
          title="会话私有 RAG 知识库"
          sub-title="会话专属知识库功能正在开发中，将支持为特定会话注入专属知识。"
        >
          <template #extra>
            <div class="feature-list">
              <el-tag type="info" effect="plain">会话隔离</el-tag>
              <el-tag type="info" effect="plain">动态注入</el-tag>
              <el-tag type="info" effect="plain">权重控制</el-tag>
            </div>
          </template>
        </el-result>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const activeTab = ref('global')
</script>

<style scoped>
.page-container {
  padding: 24px;
  max-width: 900px;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 24px;
}

.page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}

.wip-badge {
  font-size: 12px;
}

.feature-list {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: center;
}
</style>
