<template>
  <div class="dna-file-panel">
    <div class="tab-toolbar">
      <el-button type="primary" :icon="Plus" size="small" @click="openNewDialog">新建文件</el-button>
      <template v-if="scope !== 'session'">
        <el-button :icon="Download" size="small" :loading="exporting" @click="handleExport">导出 Markdown</el-button>
        <el-button :icon="Upload" size="small" :loading="importing" @click="triggerImport">导入 Markdown</el-button>
      </template>
      <input ref="importFileInputRef" type="file" accept=".md,text/plain,text/markdown" style="display:none" @change="onImportFileSelected" />
    </div>

    <div v-if="loading" class="tab-loading"><el-skeleton :rows="3" animated /></div>
    <el-empty v-else-if="files.length === 0" description="暂无 DNA 文件" :image-size="80" />
    <el-table v-else :data="files" style="width:100%" size="small">
      <el-table-column prop="category" label="分类" width="120" />
      <el-table-column prop="fileName" label="文件名" width="180" />
      <el-table-column label="内容预览">
        <template #default="{ row }">
          <span class="file-preview">{{ truncate(row.content, 60) }}</span>
        </template>
      </el-table-column>
      <el-table-column label="更新时间" width="140">
        <template #default="{ row }">{{ formatDate(row.updatedAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="openEditDialog(row)">编辑</el-button>
          <el-button link size="small" @click="openSnapshotDrawer(row)">历史</el-button>
          <el-button link type="danger" size="small" @click="deleteFile(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- ── 编辑/新建 Dialog ───────────────────────────── -->
    <el-dialog
      v-model="editDialogVisible"
      :title="editingFile ? '编辑基因文件' : '新建基因文件'"
      width="640px"
    >
      <el-form label-width="80px">
        <el-form-item label="文件名" required>
          <el-input
            v-model="form.fileName"
            placeholder="如 personality.md"
            :disabled="!!editingFile"
          />
        </el-form-item>
        <el-form-item label="分类">
          <el-input v-model="form.category" placeholder="如 persona（可选）" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input
            v-model="form.content"
            type="textarea"
            :rows="10"
            placeholder="Markdown 格式，将注入 Agent SystemPrompt 上下文"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>

    <!-- ── 版本快照 Drawer ────────────────────────────── -->
    <el-drawer
      v-model="snapshotDrawerVisible"
      :title="`历史版本 — ${snapshotTarget?.fileName ?? ''}`"
      size="600px"
      direction="rtl"
    >
      <div v-if="snapshotLoading" class="tab-loading"><el-skeleton :rows="4" animated /></div>
      <el-empty v-else-if="snapshots.length === 0" description="暂无历史版本" :image-size="80" />
      <div v-else class="snapshot-list">
        <el-card
          v-for="snap in snapshots"
          :key="snap.snapshotId"
          shadow="never"
          class="snapshot-card"
          :class="{ 'snapshot-selected': previewingSnapshot?.snapshotId === snap.snapshotId }"
          @click="previewingSnapshot = snap"
        >
          <div class="snapshot-card-header">
            <span class="snapshot-time">{{ formatDate(snap.savedAt) }}</span>
            <el-button
              type="primary"
              size="small"
              :loading="restoringSnapshotId === snap.snapshotId"
              @click.stop="restoreSnapshot(snap)"
            >回滚至此版本</el-button>
          </div>
          <pre v-if="previewingSnapshot?.snapshotId === snap.snapshotId" class="snapshot-preview">{{ snap.content }}</pre>
        </el-card>
      </div>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Download, Upload } from '@element-plus/icons-vue'
import {
  listAgentDna, writeAgentDna, deleteAgentDna, listDnaSnapshots, restoreDnaSnapshot,
  listGlobalDna, writeGlobalDna, deleteGlobalDna, listGlobalDnaSnapshots, restoreGlobalDnaSnapshot,
  listSessionDna, writeSessionDna, deleteSessionDna, listSessionDnaSnapshots, restoreSessionDnaSnapshot,
  exportAgentDna, importAgentDna, exportGlobalDna, importGlobalDna,
  type GeneFile, type GeneFileSnapshot,
} from '@/services/gatewayApi'

// ── Props ─────────────────────────────────────────────────────────────────────

const props = defineProps<{
  scope: 'global' | 'agent' | 'session'
  scopeId?: string  // agentId 或 sessionId（global 时不需要）
}>()

// ── 列表状态 ──────────────────────────────────────────────────────────────────

const loading = ref(false)
const files = ref<GeneFile[]>([])

async function loadFiles() {
  if (props.scope !== 'global' && !props.scopeId) { files.value = []; return }
  loading.value = true
  try {
    if (props.scope === 'global') {
      files.value = await listGlobalDna()
    } else if (props.scope === 'agent') {
      files.value = await listAgentDna(props.scopeId!)
    } else {
      files.value = await listSessionDna(props.scopeId!)
    }
  } finally {
    loading.value = false
  }
}

watch([() => props.scope, () => props.scopeId], loadFiles, { immediate: true })

// ── 编辑/新建 ─────────────────────────────────────────────────────────────────

const editDialogVisible = ref(false)
const saving = ref(false)
const editingFile = ref<GeneFile | null>(null)
const form = ref({ fileName: '', category: '', content: '' })

function openNewDialog() {
  editingFile.value = null
  form.value = { fileName: '', category: '', content: '' }
  editDialogVisible.value = true
}

function openEditDialog(g: GeneFile) {
  editingFile.value = g
  form.value = { fileName: g.fileName, category: g.category, content: g.content }
  editDialogVisible.value = true
}

async function save() {
  if (!form.value.fileName.trim()) { ElMessage.warning('请填写文件名'); return }
  saving.value = true
  try {
    if (props.scope === 'global') {
      await writeGlobalDna(form.value.fileName, form.value.content, form.value.category)
    } else if (props.scope === 'agent') {
      await writeAgentDna(props.scopeId!, form.value.fileName, form.value.content, form.value.category)
    } else {
      await writeSessionDna(props.scopeId!, form.value.fileName, form.value.content, form.value.category)
    }
    ElMessage.success('已保存')
    editDialogVisible.value = false
    await loadFiles()
  } finally {
    saving.value = false
  }
}

async function deleteFile(g: GeneFile) {
  await ElMessageBox.confirm(`确定删除「${g.fileName}」？`, '删除确认', {
    confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning',
  })
  try {
    if (props.scope === 'global') {
      await deleteGlobalDna(g.fileName, g.category)
    } else if (props.scope === 'agent') {
      await deleteAgentDna(props.scopeId!, g.fileName, g.category)
    } else {
      await deleteSessionDna(props.scopeId!, g.fileName, g.category)
    }
    ElMessage.success('已删除')
    await loadFiles()
  } catch {
    // 失败由全局拦截器展示
  }
}

// ── 版本快照 ──────────────────────────────────────────────────────────────────

const snapshotDrawerVisible = ref(false)
const snapshotLoading = ref(false)
const snapshotTarget = ref<GeneFile | null>(null)
const snapshots = ref<GeneFileSnapshot[]>([])
const previewingSnapshot = ref<GeneFileSnapshot | null>(null)
const restoringSnapshotId = ref<string | null>(null)

async function openSnapshotDrawer(g: GeneFile) {
  snapshotTarget.value = g
  previewingSnapshot.value = null
  snapshotDrawerVisible.value = true
  snapshotLoading.value = true
  try {
    if (props.scope === 'global') {
      snapshots.value = await listGlobalDnaSnapshots(g.fileName, g.category)
    } else if (props.scope === 'agent') {
      snapshots.value = await listDnaSnapshots(props.scopeId!, g.fileName, g.category)
    } else {
      snapshots.value = await listSessionDnaSnapshots(props.scopeId!, g.fileName, g.category)
    }
  } finally {
    snapshotLoading.value = false
  }
}

async function restoreSnapshot(snap: GeneFileSnapshot) {
  if (!snapshotTarget.value) return
  await ElMessageBox.confirm(
    `确定将「${snapshotTarget.value.fileName}」回滚至 ${formatDate(snap.savedAt)} 的版本？当前内容将被保存为新快照。`,
    '回滚确认',
    { confirmButtonText: '确认回滚', cancelButtonText: '取消', type: 'warning' }
  )
  restoringSnapshotId.value = snap.snapshotId
  try {
    if (props.scope === 'global') {
      await restoreGlobalDnaSnapshot(snapshotTarget.value.fileName, snap.snapshotId, snapshotTarget.value.category)
    } else if (props.scope === 'agent') {
      await restoreDnaSnapshot(props.scopeId!, snapshotTarget.value.fileName, snap.snapshotId, snapshotTarget.value.category)
    } else {
      await restoreSessionDnaSnapshot(props.scopeId!, snapshotTarget.value.fileName, snap.snapshotId, snapshotTarget.value.category)
    }
    ElMessage.success('已回滚至历史版本')
    snapshotDrawerVisible.value = false
    await loadFiles()
  } catch {
    // 失败由全局拦截器展示
  } finally {
    restoringSnapshotId.value = null
  }
}
// ── 导出 Markdown ───────────────────────────────────────────────────────────────

const exporting = ref(false)

async function handleExport() {
  if (props.scope === 'agent' && !props.scopeId) { ElMessage.warning('请先选择 Agent'); return }
  exporting.value = true
  try {
    const markdown = props.scope === 'global'
      ? await exportGlobalDna()
      : await exportAgentDna(props.scopeId!)
    const scopeLabel = props.scope === 'global' ? 'global' : props.scopeId
    const fileName = `dna-export-${scopeLabel}-${new Date().toISOString().slice(0, 10)}.md`
    const blob = new Blob([markdown], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = fileName
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success(`已导出 ${fileName}`)
  } finally {
    exporting.value = false
  }
}

// ── 导入 Markdown ───────────────────────────────────────────────────────────────

const importing = ref(false)
const importFileInputRef = ref<HTMLInputElement | null>(null)

function triggerImport() {
  importFileInputRef.value?.click()
}

async function onImportFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (props.scope === 'agent' && !props.scopeId) { ElMessage.warning('请先选择 Agent'); return }

  const content = await file.text()
  input.value = '' // 重置，允许再次选择同一文件
  importing.value = true
  try {
    const resp = props.scope === 'global'
      ? await importGlobalDna(content)
      : await importAgentDna(props.scopeId!, content)

    const { imported, total, entries } = resp
    const failedEntries = entries.filter(e => !e.success)

    if (failedEntries.length === 0) {
      ElMessage.success(`导入完成：共 ${total} 个文件全部成功`)
    } else {
      const failDetail = failedEntries.map(e => `• ${e.category ? e.category + '/' : ''}${e.fileName}: ${e.error}`).join('\n')
      await ElMessageBox.alert(
        `导入完成：${imported}/${total} 成功，${failedEntries.length} 个失败\n\n${failDetail}`,
        '导入结果',
        { type: failedEntries.length < total ? 'warning' : 'error', confirmButtonText: '确定' }
      )
    }
    await loadFiles()
  } finally {
    importing.value = false
  }
}
// ── 工具函数 ──────────────────────────────────────────────────────────────────

function truncate(str: string, len: number) {
  return str.length > len ? str.slice(0, len) + '…' : str
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false }).slice(0, 16)
}
</script>

<style scoped>
.dna-file-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.tab-toolbar {
  display: flex;
  gap: 8px;
  align-items: center;
}

.tab-loading {
  padding: 16px 0;
}

.file-preview {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.snapshot-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.snapshot-card {
  cursor: pointer;
  border: 1px solid var(--el-border-color-light);
  transition: border-color 0.2s;
}

.snapshot-card:hover {
  border-color: var(--el-color-primary-light-5);
}

.snapshot-selected {
  border-color: var(--el-color-primary) !important;
}

.snapshot-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.snapshot-time {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.snapshot-preview {
  margin-top: 8px;
  font-size: 12px;
  white-space: pre-wrap;
  word-break: break-all;
  background: var(--el-fill-color-light);
  padding: 8px;
  border-radius: 4px;
  max-height: 300px;
  overflow-y: auto;
}
</style>
