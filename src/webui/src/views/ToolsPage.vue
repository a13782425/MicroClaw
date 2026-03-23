<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">工具</h2>
      <el-button size="small" :loading="loading" style="margin-left: auto" @click="loadTools">刷新</el-button>
    </div>

    <el-tabs v-model="activeTab">
      <!-- ── 内置工具 ── -->
      <el-tab-pane label="内置工具" name="builtin">
        <div v-if="loading" class="tab-loading"><el-skeleton :rows="6" animated /></div>

        <template v-else>
          <div v-for="grp in builtinGroups" :key="grp.id" class="builtin-group">
            <div class="builtin-group-title">
              <el-tag type="warning" size="small" effect="plain">{{ grp.name }}</el-tag>
            </div>
            <el-table :data="grp.tools" class="tools-table">
              <el-table-column label="工具名" width="220">
                <template #default="{ row }">
                  <span class="tool-name-mono">{{ row.name }}</span>
                </template>
              </el-table-column>
              <el-table-column label="描述" prop="description" min-width="260" />
            </el-table>
          </div>
        </template>
      </el-tab-pane>

      <!-- ── 渠道工具 ── -->
      <el-tab-pane label="渠道工具" name="channel">
        <div v-if="loading" class="tab-loading"><el-skeleton :rows="6" animated /></div>

        <el-empty
          v-else-if="channelToolGroups.length === 0"
          description="暂无已启用的渠道工具"
          :image-size="60"
        />

        <template v-else>
          <div v-for="grp in channelToolGroups" :key="grp.type" class="builtin-group">
            <div class="builtin-group-title">
              <el-tag type="success" size="small" effect="plain">{{ grp.label }}</el-tag>
            </div>
            <el-table :data="grp.tools" class="tools-table">
              <el-table-column label="工具名" width="220">
                <template #default="{ row }">
                  <span class="tool-name-mono">{{ row.name }}</span>
                </template>
              </el-table-column>
              <el-table-column label="描述" prop="description" min-width="260" />
            </el-table>
          </div>
        </template>
      </el-tab-pane>

      <!-- ── MCP 工具 ── -->
      <el-tab-pane label="其他 工具" name="mcp">
        <div v-if="loading" class="tab-loading"><el-skeleton :rows="6" animated /></div>

        <el-empty
          v-else-if="mcpGroups.length === 0"
          description="暂无其他工具"
          :image-size="60"
        />

        <el-collapse v-else v-model="expandedMcpGroups">
          <el-collapse-item
            v-for="grp in mcpGroups"
            :key="grp.id"
            :name="grp.id"
          >
            <template #title>
              <div class="group-header">
                <span class="group-name">{{ grp.name }}</span>
                <el-tag
                  :type="grp.isEnabled ? 'info' : 'info'"
                  size="small"
                  effect="plain"
                >MCP</el-tag>
                <el-tag v-if="grp.loadError" type="danger" size="small" effect="plain">连接失败</el-tag>
                <span v-if="!grp.loadError" class="group-count">{{ grp.tools.length }} 个工具</span>
              </div>
            </template>

            <div class="tool-list">
              <div v-if="grp.loadError || grp.tools.length === 0" class="tool-empty">
                {{ grp.isEnabled ? (grp.loadError ? '无法连接到 MCP Server，请检查配置' : '该 Server 未暴露任何工具') : '该 Server 已停用' }}
              </div>
              <div v-for="tool in grp.tools" :key="tool.name" class="tool-item tool-item--readonly">
                <div class="tool-info">
                  <span class="tool-name-mono">{{ tool.name }}</span>
                  <span class="tool-desc">{{ tool.description ?? '—' }}</span>
                </div>
              </div>
            </div>
          </el-collapse-item>
        </el-collapse>
      </el-tab-pane>

    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { listAllTools, listChannels, getChannelTools, type GlobalToolGroup, type ChannelToolInfo } from '@/services/gatewayApi'
import { ElMessage } from 'element-plus'

const CHANNEL_LABELS: Record<string, string> = {
  feishu: '飞书',
  wecom: '企业微信',
  wechat: '微信公众号',
  web: 'Web',
}

type ChannelToolGroup = {
  type: string
  label: string
  tools: ChannelToolInfo[]
}

const activeTab = ref('builtin')
const loading = ref(false)

const builtinGroups = ref<GlobalToolGroup[]>([])
const mcpGroups = ref<GlobalToolGroup[]>([])
const expandedMcpGroups = ref<string[]>([])
const channelToolGroups = ref<ChannelToolGroup[]>([])

async function loadTools() {
  loading.value = true
  try {
    const [groups, channels] = await Promise.all([listAllTools(), listChannels()])
    builtinGroups.value = groups.filter(g => g.type === 'builtin')
    mcpGroups.value = groups.filter(g => g.type === 'mcp')
    expandedMcpGroups.value = mcpGroups.value.map(g => g.id)

    // 去重已启用渠道类型，并行加载各渠道专属工具
    const enabledTypes = [...new Set(channels.filter(c => c.isEnabled).map(c => c.channelType))]
    const results = await Promise.all(
      enabledTypes.map(async (type) => {
        try {
          const tools = await getChannelTools(type)
          return { type, label: CHANNEL_LABELS[type] ?? type, tools }
        } catch {
          return { type, label: CHANNEL_LABELS[type] ?? type, tools: [] as ChannelToolInfo[] }
        }
      })
    )
    channelToolGroups.value = results.filter(g => g.tools.length > 0)
  } catch {
    ElMessage.error('加载工具列表失败')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadTools()
})
</script>

<style scoped>
.page-container {
  padding: 24px;
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: center;
  margin-bottom: 20px;
}

.page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 600;
}

.tab-loading {
  padding: 16px 0;
}

.builtin-group {
  margin-bottom: 20px;
}

.builtin-group-title {
  margin-bottom: 8px;
}

.tools-table {
  width: 100%;
}

.group-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.group-name {
  font-weight: 600;
  font-size: 14px;
}

.group-count {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.tool-list {
  padding: 4px 0;
}

.tool-empty {
  padding: 12px 16px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.tool-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 8px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.tool-item--readonly {
  padding-left: 16px;
}

.tool-item:last-child {
  border-bottom: none;
}

.tool-name-mono {
  font-weight: 500;
  font-size: 13px;
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  color: var(--el-color-primary);
  min-width: 180px;
  flex-shrink: 0;
}

.tool-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.tool-desc {
  font-size: 13px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
}
</style>
