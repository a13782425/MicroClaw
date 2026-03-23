<template>
  <div class="page-container">
    <div class="page-header">
      <h2 class="page-title">工具</h2>
    </div>

    <el-tabs v-model="activeTab">
      <!-- ── 内置工具 ── -->
      <el-tab-pane label="内置工具" name="builtin">
        <el-table :data="builtinTools" class="tools-table">
          <el-table-column label="工具名" prop="name" width="220">
            <template #default="{ row }">
              <span class="tool-name-mono">{{ row.name }}</span>
            </template>
          </el-table-column>
          <el-table-column label="分组" width="130">
            <template #default="{ row }">
              <el-tag type="warning" size="small" effect="plain">{{ row.group }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="描述" prop="description" min-width="260" />
        </el-table>
      </el-tab-pane>

      <!-- ── 其他工具 ── -->
      <el-tab-pane label="其他工具" name="others">
        <div class="tab-toolbar">
          <el-button size="small" :loading="loadingOthers" @click="loadOtherTools">刷新</el-button>
        </div>

        <div v-if="loadingOthers" class="tab-loading"><el-skeleton :rows="6" animated /></div>

        <el-empty
          v-else-if="otherGroups.length === 0"
          description="暂无 MCP 工具或技能"
          :image-size="60"
        />

        <el-collapse v-else v-model="expandedGroups">
          <el-collapse-item
            v-for="grp in otherGroups"
            :key="grp.id"
            :name="grp.id"
          >
            <template #title>
              <div class="group-header">
                <span class="group-name">{{ grp.name }}</span>
                <el-tag
                  :type="grp.sourceType === 'MCP' ? 'info' : 'success'"
                  size="small"
                  effect="plain"
                >{{ grp.sourceType }}</el-tag>
                <span class="group-count">{{ grp.tools.length }} 个工具</span>
              </div>
            </template>

            <div class="tool-list">
              <div v-for="tool in grp.tools" :key="tool.id" class="tool-item">
                <el-switch
                  :model-value="tool.isEnabled"
                  size="small"
                  :loading="tool.toggling"
                  @change="(v: boolean) => toggleTool(grp, tool, v)"
                />
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
import {
  listMcpServers, listMcpServerTools, updateMcpServer,
  listSkills, updateSkill,
  type McpServerConfig, type McpToolInfo, type SkillConfig,
} from '@/services/gatewayApi'
import { ElMessage } from 'element-plus'

const activeTab = ref('builtin')

// ── 内置工具（固定列表）───────────────────────────────────────────────────────
const builtinTools = [
  { name: 'schedule_cron',        group: 'CronTools',   description: '创建或更新定时任务，指定 cron 表达式和触发 Prompt' },
  { name: 'list_cron_jobs',       group: 'CronTools',   description: '列出当前 Session 绑定的所有定时任务' },
  { name: 'delete_cron_job',      group: 'CronTools',   description: '删除指定 ID 的定时任务' },
  { name: 'create_sub_agent',     group: 'SubAgent',    description: '创建子代理会话，继承父会话 DNA，分配独立任务' },
  { name: 'send_to_sub_agent',    group: 'SubAgent',    description: '向已创建的子代理发送消息并获取回复' },
  { name: 'list_sub_agents',      group: 'SubAgent',    description: '列出当前父会话下所有子代理会话' },
  { name: 'import_feishu_doc',    group: 'FeishuTools', description: '将飞书文档内容导入为会话 DNA 文件' },
  { name: 'write_session_memory', group: 'Memory',      description: '向当前会话长期记忆写入重要信息' },
]

// ── 其他工具（MCP + 技能）────────────────────────────────────────────────────
interface OtherTool {
  id: string
  name: string
  description?: string
  isEnabled: boolean
  toggling?: boolean
  sourceId: string
  sourceType: 'MCP' | 'Skill'
}

interface OtherGroup {
  id: string
  name: string
  sourceType: 'MCP' | 'Skill'
  tools: OtherTool[]
}

const loadingOthers = ref(false)
const otherGroups = ref<OtherGroup[]>([])
const expandedGroups = ref<string[]>([])

async function loadOtherTools() {
  loadingOthers.value = true
  otherGroups.value = []
  try {
    const [servers, skills] = await Promise.all([listMcpServers(), listSkills()])

    // MCP groups：每个 Server 作为一个分组
    const mcpGroups: OtherGroup[] = []
    await Promise.all((servers as McpServerConfig[]).map(async (srv) => {
      try {
        const tools: McpToolInfo[] = await listMcpServerTools(srv.id)
        if (tools.length > 0) {
          mcpGroups.push({
            id: `mcp-${srv.id}`,
            name: `MCP: ${srv.name}`,
            sourceType: 'MCP',
            tools: tools.map((t) => ({
              id: `${srv.id}:${t.name}`,
              name: t.name,
              description: t.description,
              isEnabled: srv.isEnabled,
              sourceId: srv.id,
              sourceType: 'MCP' as const,
            })),
          })
        }
      } catch {
        // 忽略单个 server 加载失败
      }
    }))

    // Skill groups：按技能类型分组
    const skillTypeMap: Record<string, OtherGroup> = {}
    for (const sk of skills as SkillConfig[]) {
      const key = sk.skillType ?? 'Unknown'
      if (!skillTypeMap[key]) {
        skillTypeMap[key] = {
          id: `skill-${key}`,
          name: `Skill: ${key}`,
          sourceType: 'Skill',
          tools: [],
        }
      }
      skillTypeMap[key].tools.push({
        id: sk.id,
        name: sk.name,
        description: sk.description,
        isEnabled: sk.isEnabled,
        sourceId: sk.id,
        sourceType: 'Skill',
      })
    }

    otherGroups.value = [...mcpGroups, ...Object.values(skillTypeMap)]
    expandedGroups.value = otherGroups.value.map((g) => g.id)
  } catch {
    ElMessage.error('加载工具列表失败')
  } finally {
    loadingOthers.value = false
  }
}

async function toggleTool(grp: OtherGroup, tool: OtherTool, enabled: boolean) {
  tool.toggling = true
  try {
    if (grp.sourceType === 'MCP') {
      // MCP：在 Server 级别控制启用/禁用，该 Server 下所有工具同步状态
      await updateMcpServer({ id: tool.sourceId, isEnabled: enabled })
      grp.tools.forEach((t) => { t.isEnabled = enabled })
    } else {
      // Skill：控制单个技能启用/禁用
      await updateSkill({ id: tool.sourceId, isEnabled: enabled })
      tool.isEnabled = enabled
    }
    ElMessage.success(enabled ? '已启用' : '已停用')
  } catch {
    // 失败由全局拦截器处理
  } finally {
    tool.toggling = false
  }
}

onMounted(() => {
  loadOtherTools()
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

.tab-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.tab-loading {
  padding: 16px 0;
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

.tool-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 8px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
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
