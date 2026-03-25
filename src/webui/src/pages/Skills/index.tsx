import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Tabs, Switch, Dialog,
  createListCollection, Select, Portal,
} from '@chakra-ui/react'
import { Plus, Trash2, Code2, FileCode2, FileText } from 'lucide-react'
import {
  listSkills, createSkill, updateSkill, deleteSkill,
  listSkillFiles, getSkillFileContent, writeSkillFile, deleteSkillFile,
  type SkillConfig, type SkillType, type SkillFileInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

const SKILL_TYPE_OPTIONS = [
  { value: 'python', label: 'Python' },
  { value: 'nodejs', label: 'Node.js' },
  { value: 'shell', label: 'Shell' },
  { value: 'csharp', label: 'C#' },
]
const skillTypeCollection = createListCollection({ items: SKILL_TYPE_OPTIONS })

function skillTypeColor(t: SkillType): string {
  return { python: 'blue', nodejs: 'green', shell: 'orange', csharp: 'purple' }[t] ?? 'gray'
}

function skillTypeLabel(t: SkillType): string {
  return { python: 'Python', nodejs: 'Node.js', shell: 'Shell', csharp: 'C#' }[t] ?? t
}

// ─── 创建弹窗 ──────────────────────────────────────────────────────────────────

interface CreateDialogProps {
  open: boolean
  onClose: () => void
  onCreated: () => void
}

function CreateDialog({ open, onClose, onCreated }: CreateDialogProps) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [skillType, setSkillType] = useState<SkillType>('python')
  const [entryPoint, setEntryPoint] = useState('')
  const [saving, setSaving] = useState(false)

  const reset = () => { setName(''); setDescription(''); setSkillType('python'); setEntryPoint('') }

  const submit = async () => {
    if (!name.trim() || !entryPoint.trim()) {
      toaster.create({ type: 'error', title: '请填写名称和入口文件' })
      return
    }
    setSaving(true)
    try {
      await createSkill({ name: name.trim(), description: description.trim() || undefined, skillType, entryPoint: entryPoint.trim(), isEnabled: true })
      toaster.create({ type: 'success', title: '技能创建成功' })
      reset()
      onCreated()
      onClose()
    } catch {
      toaster.create({ type: 'error', title: '创建失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(e) => { if (!e.open) { reset(); onClose() } }}>
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content maxW="480px">
          <Dialog.Header><Dialog.Title>新建技能</Dialog.Title></Dialog.Header>
          <Dialog.Body>
            <VStack gap="3" align="stretch">
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">名称 <Text as="span" color="red.500">*</Text></Text>
                <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="技能名称" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">类型</Text>
                <Select.Root collection={skillTypeCollection} value={[skillType]} onValueChange={(e) => setSkillType(e.value[0] as SkillType)}>
                  <Select.HiddenSelect />
                  <Select.Control><Select.Trigger><Select.ValueText /></Select.Trigger><Select.IndicatorGroup><Select.Indicator /></Select.IndicatorGroup></Select.Control>
                  <Portal><Select.Positioner><Select.Content>
                    {SKILL_TYPE_OPTIONS.map((o) => <Select.Item key={o.value} item={o}>{o.label}</Select.Item>)}
                  </Select.Content></Select.Positioner></Portal>
                </Select.Root>
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">入口文件 <Text as="span" color="red.500">*</Text></Text>
                <Input value={entryPoint} onChange={(e) => setEntryPoint(e.target.value)} placeholder="如 main.py" />
              </Box>
              <Box>
                <Text fontSize="sm" mb="1" fontWeight="medium">描述</Text>
                <Textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="功能描述（可选）" />
              </Box>
            </VStack>
          </Dialog.Body>
          <Dialog.Footer>
            <Button variant="outline" onClick={onClose}>取消</Button>
            <Button colorPalette="blue" loading={saving} onClick={submit} disabled={!name.trim() || !entryPoint.trim()}>创建</Button>
          </Dialog.Footer>
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}

// ─── 文件面板 ──────────────────────────────────────────────────────────────────

function FilesTab({ skill }: { skill: SkillConfig }) {
  const [files, setFiles] = useState<SkillFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedFile, setSelectedFile] = useState<string | null>(null)
  const [content, setContent] = useState('')
  const [contentDirty, setContentDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [newFileName, setNewFileName] = useState('')
  const [showNewFile, setShowNewFile] = useState(false)
  const [deleteFilePath, setDeleteFilePath] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const res = await listSkillFiles(skill.id)
      setFiles(res)
    } catch {
      toaster.create({ type: 'error', title: '加载文件列表失败' })
    } finally {
      setLoading(false)
    }
  }, [skill.id])

  useEffect(() => { load() }, [load])

  const selectFile = async (path: string) => {
    setSelectedFile(path)
    setContentDirty(false)
    try {
      const c = await getSkillFileContent(skill.id, path)
      setContent(c)
    } catch {
      toaster.create({ type: 'error', title: '加载文件内容失败' })
    }
  }

  const saveFile = async () => {
    if (!selectedFile) return
    setSaving(true)
    try {
      await writeSkillFile(skill.id, selectedFile, content)
      toaster.create({ type: 'success', title: '文件已保存' })
      setContentDirty(false)
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  const createFile = async () => {
    if (!newFileName.trim()) return
    try {
      await writeSkillFile(skill.id, newFileName.trim(), '')
      toaster.create({ type: 'success', title: '文件已创建' })
      setNewFileName('')
      setShowNewFile(false)
      await load()
      await selectFile(newFileName.trim())
    } catch {
      toaster.create({ type: 'error', title: '创建文件失败' })
    }
  }

  const removeFile = async (path: string) => {
    try {
      await deleteSkillFile(skill.id, path)
      toaster.create({ type: 'success', title: '文件已删除' })
      if (selectedFile === path) { setSelectedFile(null); setContent('') }
      await load()
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteFilePath(null)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Flex h="100%" minH="400px">
      {/* 左：文件列表 */}
      <Box w="200px" borderRightWidth="1px" flexShrink={0}>
        <HStack px="3" py="2" justify="space-between" borderBottomWidth="1px">
          <Text fontSize="xs" fontWeight="medium" color="gray.500">文件</Text>
          <Button size="xs" variant="ghost" onClick={() => setShowNewFile((v) => !v)}>
            <Plus size={12} />
          </Button>
        </HStack>
        {showNewFile && (
          <HStack px="2" py="1" borderBottomWidth="1px">
            <Input size="xs" placeholder="文件名" value={newFileName} onChange={(e) => setNewFileName(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') createFile() }} />
            <Button size="xs" colorPalette="blue" onClick={createFile} disabled={!newFileName.trim()}>+</Button>
          </HStack>
        )}
        {files.length === 0 && <Text p="3" fontSize="xs" color="gray.400">暂无文件</Text>}
        {files.map((f) => (
          <HStack key={f.path} px="3" py="2" cursor="pointer" _hover={{ bg: 'gray.50', _dark: { bg: 'gray.800' } }}
            bg={selectedFile === f.path ? 'blue.50' : undefined} _dark={{ bg: selectedFile === f.path ? 'blue.900' : undefined }}
            onClick={() => selectFile(f.path)}
          >
            <FileCode2 size={12} />
            <Text fontSize="xs" flex="1" truncate>{f.path}</Text>
            <Button size="xs" variant="ghost" colorPalette="red" onClick={(e) => { e.stopPropagation(); setDeleteFilePath(f.path) }}>
              <Trash2 size={10} />
            </Button>
          </HStack>
        ))}
      </Box>
      {/* 右：编辑器 */}
      <Box flex="1" p="2" overflow="hidden">
        {selectedFile ? (
          <VStack h="100%" align="stretch" gap="2">
            <HStack justify="space-between">
              <HStack>
                <FileText size={14} />
                <Text fontSize="xs" color="gray.500">{selectedFile}</Text>
              </HStack>
              {contentDirty && (
                <Button size="xs" colorPalette="blue" loading={saving} onClick={saveFile}>保存</Button>
              )}
            </HStack>
            <Textarea
              flex="1"
              fontFamily="mono"
              fontSize="xs"
              value={content}
              onChange={(e) => { setContent(e.target.value); setContentDirty(true) }}
              placeholder="文件内容..."
              rows={20}
            />
          </VStack>
        ) : (
          <Flex h="100%" align="center" justify="center">
            <Text color="gray.400" fontSize="sm">请选择左侧文件进行编辑</Text>
          </Flex>
        )}
      </Box>

      <ConfirmDialog
        open={!!deleteFilePath}
        onClose={() => setDeleteFilePath(null)}
        onConfirm={() => deleteFilePath && removeFile(deleteFilePath)}
        title="删除文件"
        description={`确认删除文件 ${deleteFilePath}？`}
        confirmText="删除"
      />
    </Flex>
  )
}

// ─── 信息面板 ──────────────────────────────────────────────────────────────────

function InfoTab({ skill, onUpdated }: { skill: SkillConfig; onUpdated: (s: SkillConfig) => void }) {
  const [description, setDescription] = useState(skill.description)
  const [entryPoint, setEntryPoint] = useState(skill.entryPoint)
  const [saving, setSaving] = useState(false)
  const dirty = description !== skill.description || entryPoint !== skill.entryPoint

  useEffect(() => {
    setDescription(skill.description)
    setEntryPoint(skill.entryPoint)
  }, [skill.id, skill.description, skill.entryPoint])

  const save = async () => {
    setSaving(true)
    try {
      await updateSkill({ id: skill.id, description: description.trim(), entryPoint: entryPoint.trim() })
      onUpdated({ ...skill, description: description.trim(), entryPoint: entryPoint.trim() })
      toaster.create({ type: 'success', title: '技能信息已保存' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Box p="4">
      <VStack gap="4" align="stretch">
        <Box>
          <Text fontSize="sm" mb="1" fontWeight="medium">技能类型</Text>
          <Badge colorPalette={skillTypeColor(skill.skillType)}>{skillTypeLabel(skill.skillType)}</Badge>
        </Box>
        <Box>
          <Text fontSize="sm" mb="1" fontWeight="medium">入口文件</Text>
          <Input value={entryPoint} onChange={(e) => setEntryPoint(e.target.value)} placeholder="如 main.py" />
        </Box>
        <Box>
          <Text fontSize="sm" mb="1" fontWeight="medium">描述</Text>
          <Textarea rows={4} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="技能描述" />
        </Box>
        <HStack justify="flex-end">
          <Button colorPalette="blue" size="sm" loading={saving} disabled={!dirty} onClick={save}>保存</Button>
        </HStack>
      </VStack>
    </Box>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function SkillsPage() {
  const [skills, setSkills] = useState<SkillConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<SkillConfig | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [toggling, setToggling] = useState(false)
  const [editingName, setEditingName] = useState(false)
  const [nameInput, setNameInput] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<SkillConfig | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listSkills()
      setSkills(data)
      if (selected) {
        const updated = data.find((s) => s.id === selected.id)
        if (updated) setSelected(updated)
      }
    } catch {
      toaster.create({ type: 'error', title: '加载技能列表失败' })
    } finally {
      setLoading(false)
    }
  }, [selected])

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const handleToggle = async (skill: SkillConfig, val: boolean) => {
    setToggling(true)
    try {
      await updateSkill({ id: skill.id, isEnabled: val })
      const updated = { ...skill, isEnabled: val }
      setSkills((prev) => prev.map((s) => s.id === skill.id ? updated : s))
      if (selected?.id === skill.id) setSelected(updated)
    } catch {
      toaster.create({ type: 'error', title: '切换失败' })
    } finally {
      setToggling(false)
    }
  }

  const handleDelete = async (skill: SkillConfig) => {
    try {
      await deleteSkill(skill.id)
      toaster.create({ type: 'success', title: '技能已删除' })
      setSkills((prev) => prev.filter((s) => s.id !== skill.id))
      if (selected?.id === skill.id) setSelected(null)
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteTarget(null)
    }
  }

  const startEditName = () => {
    if (!selected) return
    setNameInput(selected.name)
    setEditingName(true)
  }

  const saveName = async () => {
    if (!selected || !nameInput.trim()) return
    try {
      await updateSkill({ id: selected.id, name: nameInput.trim() })
      const updated = { ...selected, name: nameInput.trim() }
      setSelected(updated)
      setSkills((prev) => prev.map((s) => s.id === selected.id ? updated : s))
      toaster.create({ type: 'success', title: '名称已更新' })
    } catch {
      toaster.create({ type: 'error', title: '更新失败' })
    } finally {
      setEditingName(false)
    }
  }

  return (
    <Flex h="calc(100vh - 64px)" overflow="hidden">
      {/* 左侧：技能列表 */}
      <Box w="280px" borderRightWidth="1px" flexShrink={0} overflow="auto">
        <HStack px="4" py="3" borderBottomWidth="1px" justify="space-between">
          <Text fontWeight="semibold">技能列表</Text>
          <Button size="sm" colorPalette="blue" onClick={() => setCreateOpen(true)}><Plus size={14} /></Button>
        </HStack>
        {loading && <Box p="4"><Spinner /></Box>}
        {!loading && skills.length === 0 && (
          <Text p="4" fontSize="sm" color="gray.400">暂无技能，点击右上角新建</Text>
        )}
        {skills.map((s) => (
          <HStack key={s.id} px="4" py="3" cursor="pointer" borderBottomWidth="1px"
            bg={selected?.id === s.id ? 'blue.50' : undefined}
            _dark={{ bg: selected?.id === s.id ? 'blue.900' : undefined }}
            _hover={{ bg: selected?.id === s.id ? undefined : 'gray.50', _dark: { bg: 'gray.800' } }}
            onClick={() => setSelected(s)}
          >
            <Code2 size={16} />
            <Box flex="1" overflow="hidden">
              <Text fontSize="sm" fontWeight="medium" truncate>{s.name}</Text>
              <Badge size="xs" colorPalette={skillTypeColor(s.skillType)}>{skillTypeLabel(s.skillType)}</Badge>
            </Box>
            <Box w="6px" h="6px" rounded="full" bg={s.isEnabled ? 'green.400' : 'gray.300'} flexShrink={0} />
          </HStack>
        ))}
      </Box>

      {/* 右侧：详情 */}
      {selected ? (
        <Box flex="1" overflow="auto">
          {/* 顶部工具栏 */}
          <HStack px="6" py="4" borderBottomWidth="1px" gap="3" flexWrap="wrap">
            {editingName ? (
              <HStack>
                <Input size="sm" value={nameInput} onChange={(e) => setNameInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') saveName(); if (e.key === 'Escape') setEditingName(false) }}
                  autoFocus w="200px"
                />
                <Button size="sm" colorPalette="blue" onClick={saveName}>确认</Button>
                <Button size="sm" variant="outline" onClick={() => setEditingName(false)}>取消</Button>
              </HStack>
            ) : (
              <Text fontWeight="semibold" fontSize="lg" cursor="pointer" onClick={startEditName} _hover={{ textDecoration: 'underline' }}>
                {selected.name}
              </Text>
            )}
            <Switch.Root
              size="sm"
              checked={selected.isEnabled}
              disabled={toggling}
              onCheckedChange={(e) => handleToggle(selected, e.checked)}
            >
              <Switch.HiddenInput />
              <Switch.Control><Switch.Thumb /></Switch.Control>
              <Switch.Label fontSize="sm">{selected.isEnabled ? '已启用' : '已禁用'}</Switch.Label>
            </Switch.Root>
            <Button size="sm" colorPalette="red" variant="outline" onClick={() => setDeleteTarget(selected)}>
              <Trash2 size={14} />删除
            </Button>
          </HStack>
          {/* Tabs */}
          <Tabs.Root defaultValue="info">
            <Tabs.List px="6">
              <Tabs.Trigger value="info">信息</Tabs.Trigger>
              <Tabs.Trigger value="files">文件</Tabs.Trigger>
            </Tabs.List>
            <Tabs.Content value="info">
              <InfoTab skill={selected} onUpdated={(s) => { setSelected(s); setSkills((prev) => prev.map((x) => x.id === s.id ? s : x)) }} />
            </Tabs.Content>
            <Tabs.Content value="files">
              <FilesTab skill={selected} />
            </Tabs.Content>
          </Tabs.Root>
        </Box>
      ) : (
        <Flex flex="1" align="center" justify="center">
          <Text color="gray.400">请从左侧选择技能</Text>
        </Flex>
      )}

      <CreateDialog open={createOpen} onClose={() => setCreateOpen(false)} onCreated={load} />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除技能"
        description={`确认删除技能「${deleteTarget?.name}」？`}
        confirmText="删除"
      />
    </Flex>
  )
}
