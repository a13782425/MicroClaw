import { useState, useEffect, useCallback } from 'react'
import {
  Box, Flex, Text, Input, Spinner, Textarea, Button, HStack,
} from '@chakra-ui/react'
import { toaster } from '@/components/ui/toaster'
import {
  listSessionDna,
  updateSessionDna,
  importSessionDnaFromFeishu,
  type SessionInfo,
  type SessionDnaFileInfo,
} from '@/api/gateway'

export function SessionDnaTab({ session }: { session: SessionInfo }) {
  const [files, setFiles] = useState<SessionDnaFileInfo[]>([])
  const [loading, setLoading] = useState(false)
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [activeFile, setActiveFile] = useState<string | null>(null)
  const [feishuUrl, setFeishuUrl] = useState('')
  const [importing, setImporting] = useState(false)

  useEffect(() => {
    setActiveFile(null)
  }, [session.id])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await listSessionDna(session.id)
      setFiles(data)
      const initialEdits: Record<string, string> = {}
      data.forEach((file) => { initialEdits[file.fileName] = file.content })
      setEdits(initialEdits)
      if (data.length > 0) {
        const nextActiveFile = activeFile && data.some((file) => file.fileName === activeFile)
          ? activeFile
          : data[0].fileName
        setActiveFile(nextActiveFile)
      } else {
        setActiveFile(null)
      }
    } catch {
      toaster.create({ type: 'error', title: '加载 DNA 文件失败' })
    } finally {
      setLoading(false)
    }
  }, [session.id, activeFile])

  useEffect(() => { load() }, [session.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const save = async (fileName: string) => {
    setSaving(true)
    try {
      await updateSessionDna(session.id, fileName, edits[fileName] ?? '')
      toaster.create({ type: 'success', title: '保存成功' })
    } catch {
      toaster.create({ type: 'error', title: '保存失败' })
    } finally {
      setSaving(false)
    }
  }

  const importFeishu = async () => {
    if (!feishuUrl.trim() || !activeFile) return
    setImporting(true)
    try {
      const result = await importSessionDnaFromFeishu(session.id, feishuUrl.trim(), activeFile)
      toaster.create({ type: 'success', title: `导入成功，共 ${result.charCount} 字` })
      setEdits((prev) => ({ ...prev, [activeFile]: result.file.content }))
      setFeishuUrl('')
    } catch {
      toaster.create({ type: 'error', title: '飞书导入失败' })
    } finally {
      setImporting(false)
    }
  }

  if (loading) return <Box p="4"><Spinner /></Box>
  if (files.length === 0) return <Box p="4"><Text color="gray.500" fontSize="sm">暂无 DNA 文件</Text></Box>

  const currentFile = files.find((file) => file.fileName === activeFile)

  return (
    <Flex h="100%" direction="column">
      <HStack gap="1" px="3" pt="3" flexWrap="wrap">
        {files.map((file) => (
          <Button
            key={file.fileName}
            size="xs"
            variant={activeFile === file.fileName ? 'solid' : 'outline'}
            colorPalette="blue"
            onClick={() => setActiveFile(file.fileName)}
          >
            {file.fileName.replace('.md', '')}
          </Button>
        ))}
      </HStack>

      {currentFile && (
        <Flex direction="column" flex="1" p="3" gap="2" overflow="hidden">
          {currentFile.description && (
            <Text fontSize="xs" color="gray.500">{currentFile.description}</Text>
          )}
          <Textarea
            flex="1"
            fontFamily="mono"
            fontSize="sm"
            resize="none"
            value={edits[currentFile.fileName] ?? ''}
            onChange={(e) => setEdits((prev) => ({ ...prev, [currentFile.fileName]: e.target.value }))}
            spellCheck={false}
          />
          <HStack>
            <Button size="sm" colorPalette="blue" loading={saving} onClick={() => save(currentFile.fileName)}>
              保存
            </Button>
            {session.channelType === 'feishu' && (
              <>
                <Input
                  size="sm"
                  flex="1"
                  placeholder="飞书文档 URL 或 Token"
                  value={feishuUrl}
                  onChange={(e) => setFeishuUrl(e.target.value)}
                />
                <Button size="sm" variant="outline" loading={importing} onClick={importFeishu}>
                  飞书导入
                </Button>
              </>
            )}
          </HStack>
        </Flex>
      )}
    </Flex>
  )
}
