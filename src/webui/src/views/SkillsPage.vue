<template>
  <div class="skills-layout">
    <!-- ── 左侧技能列表 ──────────────────────────────────── -->
    <div class="skills-sidebar">
      <div class="sidebar-header">
        <span class="sidebar-title">技能</span>
        <el-button size="small" :icon="Plus" circle title="添加技能" @click="openCreateDialog" />
      </div>

      <div v-if="loading" class="sidebar-loading">
        <el-skeleton :rows="3" animated />
      </div>

      <div v-else-if="skills.length === 0" class="sidebar-empty">
        <el-empty description="暂无技能" :image-size="60" />
      </div>

      <div v-else class="skill-list">
        <div
          v-for="s in skills"
          :key="s.id"
          class="skill-item"
          :class="{ active: selectedSkill?.id === s.id, 'is-disabled': !s.isEnabled }"
          @click="selectSkill(s)"
        >
          <div class="skill-avatar">{{ skillTypeIcon(s.skillType) }}</div>
          <div class="skill-info">
            <span class="skill-name">{{ s.name }}</span>
            <el-tag size="small" :type="typeTagType(s.skillType)" effect="plain">{{ s.skillType }}</el-tag>
          </div>
          <div class="skill-status-dot" :class="s.isEnabled ? 'enabled' : 'offline'" />
        </div>
      </div>
    </div>

    <!-- ── 右侧详情面板 ──────────────────────────────────── -->
    <div v-if="selectedSkill" class="skills-detail">
      <!-- Header -->
      <div class="detail-header">
        <div class="detail-icon">{{ skillTypeIcon(selectedSkill.skillType) }}</div>
        <div class="detail-title-block">
          <div class="detail-name-row">
            <h2 class="detail-name">{{ selectedSkill.name }}</h2>
            <el-tag :type="typeTagType(selectedSkill.skillType)" effect="plain">{{ selectedSkill.skillType }}</el-tag>
          </div>
          <div class="detail-subtitle">{{ selectedSkill.description || '（无描述）' }}</div>
        </div>
        <div class="detail-actions">
          <el-switch
            v-model="selectedSkill.isEnabled"
            active-text="启用"
            inactive-text="停用"
            @change="(val: boolean) => onToggleEnabled(selectedSkill!, val)"
          />
          <el-button :icon="Edit" @click="openEditDialog(selectedSkill)">编辑</el-button>
          <el-button :icon="Delete" type="danger" plain @click="confirmDelete(selectedSkill)">删除</el-button>
        </div>
      </div>

      <!-- Tabs -->
      <el-tabs v-model="activeTab" class="detail-tabs">
        <!-- 概览 -->
        <el-tab-pane label="概览" name="overview">
          <div class="overview-grid">
            <div class="overview-card">
              <div class="ov-label">类型</div>
              <div class="ov-value">{{ selectedSkill.skillType }}</div>
            </div>
            <div class="overview-card">
              <div class="ov-label">状态</div>
              <div class="ov-value">
                <el-tag :type="selectedSkill.isEnabled ? 'success' : 'info'">
                  {{ selectedSkill.isEnabled ? '启用' : '停用' }}
                </el-tag>
              </div>
            </div>
            <div class="overview-card">
              <div class="ov-label">创建时间</div>
              <div class="ov-value">{{ formatDate(selectedSkill.createdAtUtc) }}</div>
            </div>
          </div>
          <div class="ov-entry-block">
            <div class="ov-label">入口文件</div>
            <code class="ov-entry">{{ selectedSkill.entryPoint }}</code>
          </div>
          <div v-if="selectedSkill.description" class="ov-desc-block">
            <div class="ov-label">描述</div>
            <pre class="ov-desc">{{ selectedSkill.description }}</pre>
          </div>
        </el-tab-pane>

        <!-- 文件 -->
        <el-tab-pane label="文件" name="files">
          <div class="tab-toolbar">
            <el-button type="primary" :icon="Plus" size="small" @click="openNewFileDialog">新建文件</el-button>
          </div>
          <div v-if="filesLoading" class="tab-loading"><el-skeleton :rows="3" animated /></div>
          <el-empty v-else-if="skillFiles.length === 0" description="暂无文件" :image-size="80" />
          <el-table v-else :data="skillFiles" style="width:100%" size="small">
            <el-table-column prop="path" label="文件路径" />
            <el-table-column label="大小" width="120">
              <template #default="{ row }">{{ formatSize(row.sizeBytes) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="140">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openFileEditor(row)">编辑</el-button>
                <el-button link type="danger" size="small" @click="deleteFile(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </div>

    <!-- 未选中占位 -->
    <div v-else class="skills-detail skills-detail--empty">
      <el-empty description="从左侧选择一个技能" :image-size="80" />
    </div>

    <!-- ── 创建/编辑 Dialog ──────────────────────────────── -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingSkill ? '编辑技能' : '添加技能'"
      width="520px"
      :close-on-click-modal="false"
    >
      <el-form :model="form" label-width="90px" label-position="left">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="技能名称" />
        </el-form-item>
        <el-form-item label="类型" required>
          <el-select v-model="form.skillType" style="width:100%">
            <el-option label="Python" value="python" />
            <el-option label="Node.js" value="nodejs" />
            <el-option label="Shell" value="shell" />
          </el-select>
        </el-form-item>
        <el-form-item label="入口文件" required>
          <el-input v-model="form.entryPoint" placeholder="如 main.py 或 index.js" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="3" placeholder="技能说明（可选）" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveSkill">保存</el-button>
      </template>
    </el-dialog>

    <!-- ── 文件编辑 Dialog ──────────────────────────────── -->
    <el-dialog
      v-model="fileDialogVisible"
      :title="editingFilePath ? `编辑 ${editingFilePath}` : '新建文件'"
      width="680px"
      :close-on-click-modal="false"
    >
      <el-form label-width="80px">
        <el-form-item v-if="!editingFilePath" label="文件名" required>
          <el-input v-model="fileForm.fileName" placeholder="如 main.py" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input
            v-model="fileForm.content"
            type="textarea"
            :rows="16"
            placeholder="文件内容"
            style="font-family: monospace; font-size: 13px"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="fileDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="fileSaving" @click="saveFile">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Edit, Delete } from '@element-plus/icons-vue'
import {
  listSkills, createSkill, updateSkill, deleteSkill,
  listSkillFiles, getSkillFileContent, writeSkillFile, deleteSkillFile,
  type SkillConfig, type SkillFileInfo, type SkillType,
} from '@/services/gatewayApi'

// ── 列表状态 ──────────────────────────────────────────────────────────────────
const loading = ref(false)
const skills = ref<SkillConfig[]>([])

async function loadSkills() {
  loading.value = true
  try {
    skills.value = await listSkills()
  } finally {
    loading.value = false
  }
}

onMounted(loadSkills)

function skillTypeIcon(type: SkillType): string {
  const map: Record<SkillType, string> = { python: '🐍', nodejs: '⬡', shell: '⌨' }
  return map[type] ?? '📦'
}

function typeTagType(type: SkillType): 'success' | 'warning' | 'info' {
  const map: Record<SkillType, 'success' | 'warning' | 'info'> = {
    python: 'success', nodejs: 'warning', shell: 'info',
  }
  return map[type] ?? 'info'
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false }).slice(0, 16)
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`
}

// ── 选中 ──────────────────────────────────────────────────────────────────────
const selectedSkill = ref<SkillConfig | null>(null)
const activeTab = ref('overview')

function selectSkill(s: SkillConfig) {
  if (selectedSkill.value?.id !== s.id) {
    selectedSkill.value = s
    activeTab.value = 'overview'
    skillFiles.value = []
  }
}

watch(activeTab, async (tab) => {
  if (tab === 'files' && selectedSkill.value) await loadFiles()
})

// ── 启用/停用 ─────────────────────────────────────────────────────────────────
async function onToggleEnabled(s: SkillConfig, val: boolean) {
  try {
    await updateSkill({ id: s.id, isEnabled: val })
    ElMessage.success(val ? '已启用' : '已停用')
    await loadSkills()
    const updated = skills.value.find(sk => sk.id === s.id)
    if (updated) selectedSkill.value = updated
  } catch {
    s.isEnabled = !val
    ElMessage.error('操作失败')
  }
}

// ── 创建/编辑 Dialog ──────────────────────────────────────────────────────────
interface SkillForm {
  name: string
  description: string
  skillType: SkillType
  entryPoint: string
  isEnabled: boolean
}

const dialogVisible = ref(false)
const saving = ref(false)
const editingSkill = ref<SkillConfig | null>(null)

const form = ref<SkillForm>({
  name: '',
  description: '',
  skillType: 'python',
  entryPoint: 'main.py',
  isEnabled: true,
})

function openCreateDialog() {
  editingSkill.value = null
  form.value = { name: '', description: '', skillType: 'python', entryPoint: 'main.py', isEnabled: true }
  dialogVisible.value = true
}

function openEditDialog(s: SkillConfig) {
  editingSkill.value = s
  form.value = {
    name: s.name,
    description: s.description,
    skillType: s.skillType,
    entryPoint: s.entryPoint,
    isEnabled: s.isEnabled,
  }
  dialogVisible.value = true
}

async function saveSkill() {
  if (!form.value.name.trim()) { ElMessage.warning('请填写技能名称'); return }
  if (!form.value.entryPoint.trim()) { ElMessage.warning('请填写入口文件'); return }

  saving.value = true
  try {
    if (editingSkill.value) {
      await updateSkill({
        id: editingSkill.value.id,
        name: form.value.name,
        description: form.value.description,
        skillType: form.value.skillType,
        entryPoint: form.value.entryPoint,
        isEnabled: form.value.isEnabled,
      })
      ElMessage.success('已保存')
    } else {
      const { id } = await createSkill({
        name: form.value.name,
        description: form.value.description,
        skillType: form.value.skillType,
        entryPoint: form.value.entryPoint,
        isEnabled: form.value.isEnabled,
      })
      ElMessage.success('创建成功')
      dialogVisible.value = false
      await loadSkills()
      const created = skills.value.find(s => s.id === id)
      if (created) selectSkill(created)
      return
    }
    dialogVisible.value = false
    await loadSkills()
    if (editingSkill.value) {
      const updated = skills.value.find(s => s.id === editingSkill.value!.id)
      if (updated) selectedSkill.value = updated
    }
  } catch {
    ElMessage.error('保存失败')
  } finally {
    saving.value = false
  }
}

async function confirmDelete(s: SkillConfig) {
  await ElMessageBox.confirm(`确定删除技能「${s.name}」？`, '删除确认', {
    confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning',
  })
  try {
    await deleteSkill(s.id)
    ElMessage.success('已删除')
    if (selectedSkill.value?.id === s.id) selectedSkill.value = null
    await loadSkills()
  } catch {
    ElMessage.error('删除失败')
  }
}

// ── 文件管理 ──────────────────────────────────────────────────────────────────
const filesLoading = ref(false)
const skillFiles = ref<SkillFileInfo[]>([])

async function loadFiles() {
  if (!selectedSkill.value) return
  filesLoading.value = true
  try {
    skillFiles.value = await listSkillFiles(selectedSkill.value.id)
  } finally {
    filesLoading.value = false
  }
}

const fileDialogVisible = ref(false)
const fileSaving = ref(false)
const editingFilePath = ref<string | null>(null)
const fileForm = ref({ fileName: '', content: '' })

function openNewFileDialog() {
  editingFilePath.value = null
  fileForm.value = { fileName: '', content: '' }
  fileDialogVisible.value = true
}

async function openFileEditor(f: SkillFileInfo) {
  if (!selectedSkill.value) return
  editingFilePath.value = f.path
  fileForm.value = { fileName: f.path, content: '' }
  fileDialogVisible.value = true
  try {
    const content = await getSkillFileContent(selectedSkill.value.id, f.path)
    fileForm.value.content = content
  } catch {
    ElMessage.error('加载文件内容失败')
  }
}

async function saveFile() {
  const fileName = editingFilePath.value ?? fileForm.value.fileName.trim()
  if (!fileName) { ElMessage.warning('请填写文件名'); return }
  if (!selectedSkill.value) return

  fileSaving.value = true
  try {
    await writeSkillFile(selectedSkill.value.id, fileName, fileForm.value.content)
    ElMessage.success('已保存')
    fileDialogVisible.value = false
    await loadFiles()
  } catch {
    ElMessage.error('保存失败')
  } finally {
    fileSaving.value = false
  }
}

async function deleteFile(f: SkillFileInfo) {
  if (!selectedSkill.value) return
  await ElMessageBox.confirm(`确定删除「${f.path}」？`, '删除确认', {
    confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning',
  })
  try {
    await deleteSkillFile(selectedSkill.value.id, f.path)
    ElMessage.success('已删除')
    await loadFiles()
  } catch {
    ElMessage.error('删除失败')
  }
}
</script>

<style scoped>
.skills-layout {
  display: flex;
  height: 100%;
  overflow: hidden;
}

/* ── Sidebar ────────────────────────────────────── */
.skills-sidebar {
  width: 260px;
  flex-shrink: 0;
  border-right: 1px solid var(--el-border-color);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px;
  border-bottom: 1px solid var(--el-border-color);
}

.sidebar-title {
  font-size: 15px;
  font-weight: 600;
}

.sidebar-loading,
.sidebar-empty {
  padding: 24px;
}

.skill-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.skill-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  cursor: pointer;
  transition: background 0.15s;
}

.skill-item:hover { background: var(--el-fill-color-light); }
.skill-item.active { background: var(--el-color-primary-light-9); }
.skill-item.is-disabled { opacity: 0.6; }

.skill-avatar {
  width: 36px;
  height: 36px;
  border-radius: 8px;
  background: var(--el-fill-color);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
}

.skill-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.skill-name {
  font-size: 14px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.skill-status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.skill-status-dot.enabled { background: var(--el-color-success); }
.skill-status-dot.offline  { background: var(--el-color-info); }

/* ── Detail Panel ───────────────────────────────── */
.skills-detail {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.skills-detail--empty {
  align-items: center;
  justify-content: center;
}

.detail-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--el-border-color);
  flex-shrink: 0;
}

.detail-icon {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  background: var(--el-fill-color);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
  flex-shrink: 0;
}

.detail-title-block {
  flex: 1;
  min-width: 0;
}

.detail-name-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.detail-name {
  margin: 0 0 4px;
  font-size: 18px;
  font-weight: 700;
}

.detail-subtitle {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.detail-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.detail-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0 24px;
}

.detail-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow-y: auto;
  padding-top: 16px;
}

/* ── Overview ────────────────────────────────────── */
.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}

.overview-card {
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  padding: 12px;
}

.ov-label {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  margin-bottom: 4px;
}

.ov-value {
  font-size: 14px;
  font-weight: 600;
}

.ov-entry-block,
.ov-desc-block {
  margin-top: 12px;
}

.ov-entry {
  display: inline-block;
  margin-top: 6px;
  padding: 4px 10px;
  background: var(--el-fill-color);
  border-radius: 4px;
  font-size: 13px;
}

.ov-desc {
  margin: 8px 0 0;
  padding: 12px;
  background: var(--el-fill-color);
  border-radius: 6px;
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
}

/* ── Files Tab ────────────────────────────────────── */
.tab-toolbar {
  margin-bottom: 12px;
}

.tab-loading {
  padding: 16px 0;
}
</style>
