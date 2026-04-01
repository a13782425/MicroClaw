import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, VStack, Spinner, Input, Button,
} from '@chakra-ui/react'
import { toaster } from '@/components/ui/toaster'
import {
  getRagConfig,
  updateRagConfig,
  type RagConfig,
} from '@/api/gateway'

export function RagSettingsTab() {
  const [config, setConfig] = useState<RagConfig | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [maxStorageSizeMb, setMaxStorageSizeMb] = useState('')
  const [pruneTargetPercent, setPruneTargetPercent] = useState('')

  const fetchConfig = useCallback(async () => {
    setLoading(true)
    try {
      const data = await getRagConfig()
      setConfig(data)
      setMaxStorageSizeMb(String(data.maxStorageSizeMb))
      setPruneTargetPercent(String(Math.round(data.pruneTargetPercent * 100)))
    } catch {
      toaster.create({ type: 'error', title: '加载 RAG 配置失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchConfig() }, [fetchConfig])

  const handleSave = async () => {
    const sizeMb = Number(maxStorageSizeMb)
    const prunePercentValue = Number(pruneTargetPercent)
    const prunePct = prunePercentValue / 100

    if (isNaN(sizeMb) || sizeMb <= 0) {
      toaster.create({ type: 'error', title: '最大存储大小必须大于 0' })
      return
    }
    if (isNaN(prunePercentValue) || prunePercentValue <= 0 || prunePercentValue > 100) {
      toaster.create({ type: 'error', title: '清理目标比例必须在 1-100 之间' })
      return
    }

    setSaving(true)
    try {
      await updateRagConfig({ maxStorageSizeMb: sizeMb, pruneTargetPercent: prunePct })
      setConfig({ maxStorageSizeMb: sizeMb, pruneTargetPercent: prunePct })
      toaster.create({ type: 'success', title: 'RAG 配置已更新' })
    } catch {
      toaster.create({ type: 'error', title: '保存配置失败' })
    } finally {
      setSaving(false)
    }
  }

  if (loading && !config) {
    return (
      <Box py="12" textAlign="center">
        <Spinner size="lg" color="blue.500" />
        <Text mt="3" color="gray.500">加载中…</Text>
      </Box>
    )
  }

  return (
    <Box maxW="480px">
      <Text fontSize="sm" color="gray.500" mb="5">
        配置 RAG 自动遗忘机制。当单个会话 RAG 存储超过阈值时，系统将自动删除调用次数最低的向量分块以释放空间。
      </Text>

      <VStack gap="5" align="stretch">
        <Box>
          <Text fontSize="sm" fontWeight="medium" mb="1">最大存储大小 (MB)</Text>
          <Input
            size="sm"
            type="number"
            value={maxStorageSizeMb}
            onChange={(e) => setMaxStorageSizeMb(e.target.value)}
            placeholder="50"
          />
          <Text fontSize="xs" color="gray.400" mt="1">
            单个会话 RAG 数据库文件的最大大小，超过后触发自动清理
          </Text>
        </Box>

        <Box>
          <Text fontSize="sm" fontWeight="medium" mb="1">清理目标比例 (%)</Text>
          <Input
            size="sm"
            type="number"
            value={pruneTargetPercent}
            onChange={(e) => setPruneTargetPercent(e.target.value)}
            placeholder="80"
          />
          <Text fontSize="xs" color="gray.400" mt="1">
            清理后的目标大小占阈值的百分比（例如 80 表示清理到阈值的 80%）
          </Text>
        </Box>

        <Button size="sm" colorPalette="blue" onClick={handleSave} loading={saving} alignSelf="flex-start">
          保存配置
        </Button>
      </VStack>
    </Box>
  )
}
