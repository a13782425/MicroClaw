import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Badge, Button, HStack, VStack, Spinner,
  Tabs,
} from '@chakra-ui/react'
import { RefreshCw, Trash2, Code2, FileCode2, FileText } from 'lucide-react'
import {
  listSkills, scanSkills, deleteSkill,
  listSkillFiles, getSkillFileContent,
  type SkillConfig, type SkillFileInfo,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

//  文件面板（只读） 

function FilesTab({ skill }: { skill: SkillConfig }) {
  const [files, setFiles] = useState<SkillFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedFile, setSelectedFile] = useState<string | null>(null)
  const [content, setContent] = useState('')

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
    try {
      const c = await getSkillFileContent(skill.id, path)
      setContent(c)
    } catch {
      toaster.create({ type: 'error', title: '加载文件内容失败' })
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>

  return (
    <Flex h="100%" minH="400px">
      {/* 文件列表 */}
      <Box w="200px" borderRightWidth="1px" overflowY="auto" flexShrink={0}>
        {files.length === 0 && (
          <Text p="3" fontSize="sm" color="var(--mc-text-muted)">暂无文件</Text>
        )}
        {files.map(f => (
          <Box
            key={f.path}
            px="3" py="2" cursor="pointer"
            bg={selectedFile === f.path ? 'blue.50' : 'transparent'}
           
            _hover={{ bg: 'gray.50', _dark: { bg: 'gray.700' } }}
            onClick={() => selectFile(f.path)}
          >
            <HStack gap="1">
              {f.path.endsWith('.md') ? <FileText size={14} /> : <FileCode2 size={14} />}
              <Text fontSize="xs" truncate>{f.path}</Text>
            </HStack>
            <Text fontSize="xs" color="var(--mc-text-muted)">{(f.sizeBytes / 1024).toFixed(1)} KB</Text>
          </Box>
        ))}
      </Box>

      {/* 文件内容（只读） */}
      <Box flex="1" overflowY="auto" p="3">
        {selectedFile ? (
          <>
            <HStack mb="2" justify="space-between">
              <Text fontSize="xs" color="var(--mc-text-muted)">{selectedFile}</Text>
            </HStack>
            <Box
              as="pre"
              fontSize="xs"
              fontFamily="mono"
              whiteSpace="pre-wrap"
              wordBreak="break-all"
              p="3"
              borderWidth="1px"
              borderRadius="md"
              bg="var(--mc-surface-muted)"
             
              minH="200px"
            >
              {content}
            </Box>
          </>
        ) : (
          <Text color="var(--mc-text-muted)" fontSize="sm">请从左侧选择文件</Text>
        )}
      </Box>
    </Flex>
  )
}

//  信息面板 

function InfoTab({ skill }: { skill: SkillConfig }) {
  const rows: [string, React.ReactNode][] = [
    ['ID', <Text fontFamily="mono" fontSize="xs">{skill.id}</Text>],
    ['描述', skill.description || <Text color="var(--mc-text-muted)">无</Text>],
    ['用户可见', skill.userInvocable ? '是' : '否'],
    ['允许工具', skill.allowedTools || <Text color="var(--mc-text-muted)">全部</Text>],
    ['模型', skill.model || <Text color="var(--mc-text-muted)">继承</Text>],
    ['推理强度', skill.effort || <Text color="var(--mc-text-muted)">继承</Text>],
    ['禁止模型调用', skill.disableModelInvocation ? '是' : '否'],
    ['创建时间', new Date(skill.createdAtUtc).toLocaleString()],
  ]

  return (
    <VStack align="stretch" gap="2" p="4">
      {rows.map(([label, value]) => (
        <Flex key={label} gap="3" align="baseline">
          <Text fontSize="sm" color="var(--mc-text-muted)" w="100px" flexShrink={0}>{label}</Text>
          <Box flex="1" fontSize="sm">{value}</Box>
        </Flex>
      ))}
    </VStack>
  )
}

//  主页面 

export default function SkillsPage() {
  const [skills, setSkills] = useState<SkillConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [selected, setSelected] = useState<SkillConfig | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<SkillConfig | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setSkills(await listSkills())
    } catch {
      toaster.create({ type: 'error', title: '加载技能列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const handleScan = async () => {
    setScanning(true)
    try {
      const r = await scanSkills()
      toaster.create({ type: 'success', title: `扫描完成：共 ${r.found} 个，新增 ${r.added} 个` })
      await load()
    } catch {
      toaster.create({ type: 'error', title: '扫描失败' })
    } finally {
      setScanning(false)
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    try {
      await deleteSkill(deleteTarget.id)
      toaster.create({ type: 'success', title: '已删除' })
      if (selected?.id === deleteTarget.id) setSelected(null)
      await load()
    } catch {
      toaster.create({ type: 'error', title: '删除失败' })
    } finally {
      setDeleteTarget(null)
    }
  }

  return (
    <Box h="100%" display="flex" flexDir="column">
      {/* 头部工具栏 */}
      <HStack px="4" py="3" borderBottomWidth="1px" justify="space-between">
        <Text fontWeight="semibold">技能列表</Text>
        <HStack>
          <Button size="sm" variant="outline" onClick={handleScan} loading={scanning}>
            <RefreshCw size={14} />扫描
          </Button>
        </HStack>
      </HStack>

      <Flex flex="1" overflow="hidden">
        {/* 左侧技能列表 */}
        <Box w="240px" borderRightWidth="1px" overflowY="auto" flexShrink={0}>
          {loading && <Box p="4"><Spinner /></Box>}
          {skills.map(s => (
            <Box
              key={s.id}
              px="3" py="2" cursor="pointer"
              bg={selected?.id === s.id ? 'blue.50' : 'transparent'}
             
              _hover={{ bg: 'gray.50', _dark: { bg: 'gray.700' } }}
              onClick={() => setSelected(s)}
            >
              <HStack justify="space-between">
                <HStack gap="2">
                  <Code2 size={14} />
                  <Text fontSize="sm" fontWeight="medium">{s.name}</Text>
                </HStack>
              </HStack>
              {s.description && (
                <Text fontSize="xs" color="var(--mc-text-muted)" truncate>{s.description}</Text>
              )}
              <HStack mt="1" gap="1">
                {s.userInvocable && <Badge size="sm" colorPalette="blue">用户可见</Badge>}
                {s.disableModelInvocation && <Badge size="sm" colorPalette="orange">无模型</Badge>}
              </HStack>
            </Box>
          ))}
          {!loading && skills.length === 0 && (
            <Text p="3" fontSize="sm" color="var(--mc-text-muted)">暂无技能，请点扫描</Text>
          )}
        </Box>

        {/* 右侧详情 */}
        <Box flex="1" overflow="hidden">
          {selected ? (
            <Flex flexDir="column" h="100%">
              {/* 详情头部 */}
              <HStack px="4" py="3" borderBottomWidth="1px" justify="space-between">
                <HStack gap="2">
                  <Code2 size={16} />
                  <Text fontWeight="semibold">{selected.name}</Text>
                </HStack>
                <Button
                  size="sm" colorPalette="red" variant="ghost"
                  onClick={() => setDeleteTarget(selected)}
                >
                  <Trash2 size={14} />删除
                </Button>
              </HStack>

              {/* Tab 面板 */}
              <Tabs.Root defaultValue="info" flex="1" display="flex" flexDir="column" overflow="hidden">
                <Tabs.List px="4">
                  <Tabs.Trigger value="info">信息</Tabs.Trigger>
                  <Tabs.Trigger value="files">文件</Tabs.Trigger>
                </Tabs.List>
                <Tabs.Content value="info" flex="1" overflow="auto" p="0">
                  <InfoTab skill={selected} />
                </Tabs.Content>
                <Tabs.Content value="files" flex="1" overflow="hidden" p="0">
                  <FilesTab skill={selected} />
                </Tabs.Content>
              </Tabs.Root>
            </Flex>
          ) : (
            <Flex h="100%" align="center" justify="center">
              <Text color="var(--mc-text-muted)">请从左侧选择技能</Text>
            </Flex>
          )}
        </Box>
      </Flex>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title="删除技能"
        description={`确定要删除技能「${deleteTarget?.name}」吗？此操作不可撤销。`}
        confirmText="删除"
        onConfirm={handleDelete}
        onClose={() => setDeleteTarget(null)}
      />
    </Box>
  )
}