import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Input, Textarea, Tabs, Switch,
} from '@chakra-ui/react'
import { RefreshCw, Trash2, Code2, FileCode2, FileText } from 'lucide-react'
import {
  listSkills, scanSkills, updateSkill, deleteSkill,
  listSkillFiles, getSkillFileContent, writeSkillFile, deleteSkillFile,
  type SkillConfig, type SkillFileInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

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
            <FileCode2 size={12} />
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

function InfoTab({ skill }: { skill: SkillConfig }) {
  return (
    <Box p="4">
      <VStack gap="3" align="stretch">
        {skill.description && (
          <Box>
            <Text fontSize="sm" mb="1" fontWeight="medium" color="gray.500">描述</Text>
            <Text fontSize="sm">{skill.description}</Text>
          </Box>
        )}
        {skill.allowedTools && (
          <Box>
            <Text fontSize="sm" mb="1" fontWeight="medium" color="gray.500">自动批准工具</Text>
            <Text fontSize="sm" fontFamily="mono">{skill.allowedTools}</Text>
          </Box>
        )}
        <HStack gap="4" flexWrap="wrap">
          {skill.model && (
            <Box>
              <Text fontSize="xs" color="gray.400">模型覆盖</Text>
              <Badge size="sm">{skill.model}</Badge>
            </Box>
          )}
          {skill.effort && (
            <Box>
              <Text fontSize="xs" color="gray.400">推理强度</Text>
              <Badge size="sm" colorPalette="purple">{skill.effort}</Badge>
            </Box>
          )}
          {skill.context && (
            <Box>
              <Text fontSize="xs" color="gray.400">执行上下文</Text>
              <Badge size="sm" colorPalette="orange">{skill.context}</Badge>
            </Box>
          )}
        </HStack>
        <Box>
          <Text fontSize="xs" color="gray.400">创建时间</Text>
          <Text fontSize="sm">{new Date(skill.createdAtUtc).toLocaleString()}</Text>
        </Box>
        <Box>
          <Text fontSize="xs" color="gray.400" mb="1">提示</Text>
          <Text fontSize="xs" color="gray.500">
            技能元数据（名称、描述、指令等）通过编辑 <Text as="span" fontFamily="mono">SKILL.md</Text> 文件管理，
            切换到「文件」选项卡进行编辑。
          </Text>
        </Box>
      </VStack>
    </Box>
  )
}

// ─── 主页面 ────────────────────────────────────────────────────────────────────

export default function SkillsPage() {
  const [skills, setSkills] = useState<SkillConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [selected, setSelected] = useState<SkillConfig | null>(null)
  const [toggling, setToggling] = useState(false)
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

  const handleScan = async () => {
    setScanning(true)
    try {
      const result = await scanSkills()
      toaster.create({
        type: 'success',
        title: `扫描完成：发现 ${result.found} 个技能，新增 ${result.added} 个`,
      })
      await load()
    } catch {
      toaster.create({ type: 'error', title: '扫描失败' })
    } finally {
      setScanning(false)
    }
  }

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

  return (
    <Flex h="calc(100vh - 64px)" overflow="hidden">
      {/* 左侧：技能列表 */}
      <Box w="280px" borderRightWidth="1px" flexShrink={0} overflow="auto">
        <HStack px="4" py="3" borderBottomWidth="1px" justify="space-between">
          <Text fontWeight="semibold">技能列表</Text>
          <Button size="sm" colorPalette="blue" loading={scanning} onClick={handleScan}>
            <RefreshCw size={14} />扫描
          </Button>
        </HStack>
        {loading && <Box p="4"><Spinner /></Box>}
        {!loading && skills.length === 0 && (
          <Text p="4" fontSize="sm" color="gray.400">
            暂无技能，点击「扫描」从文件夹加载，或使用 AI 技能创建
          </Text>
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
              {s.description && (
                <Text fontSize="xs" color="gray.400" truncate>{s.description}</Text>
              )}
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
            <Text fontWeight="semibold" fontSize="lg">{selected.name}</Text>
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
              <InfoTab skill={selected} />
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

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => deleteTarget && handleDelete(deleteTarget)}
        title="删除技能"
        description={`确认删除技能「${deleteTarget?.name}」？此操作不会删除文件系统上的技能目录。`}
        confirmText="删除"
      />
    </Flex>
  )
}
