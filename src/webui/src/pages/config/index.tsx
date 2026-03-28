import { useState, useEffect, useCallback } from 'react'
import { Box, Text, Badge, SimpleGrid, Table, Input, Button, Flex, IconButton, Stack } from '@chakra-ui/react'
import { useNavigate } from 'react-router-dom'
import { Plus, Trash2 } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import {
  getSystemConfig,
  updateAgentConfig,
  updateSkillsConfig,
  type SystemConfigDto
} from '@/api/gateway'

/**
 * 系统配置页面（Config）
 *
 * 此页面主要展示：
 * 1. 快速导航卡片（跳转至各配置页）
 * 2. 关键配置项说明表格（只读参考，非编辑）
 *
 * 如需动态配置，调用 getSystemConfig / updateSystemConfig (src/api/gateway.ts)
 */

const CONFIG_REFERENCE = [
  { key: 'MICROCLAW_CONFIG_FILE', desc: '配置文件路径，默认 microclaw.json', default: 'microclaw.json' },
  { key: 'MICROCLAW_WEBUI_PATH', desc: 'WebUI 静态文件目录（Docker 用）', default: '/app/webui' },
  { key: 'OPENAI__APIKEY', desc: 'OpenAI API 密钥（环境变量注入）', default: '' },
  { key: 'CLAUDE__APIKEY', desc: 'Claude API 密钥（环境变量注入）', default: '' },
]

const QUICK_LINKS = [
  { label: '模型配置', path: '/models', desc: '管理 AI Provider 和模型参数' },
  { label: '渠道管理', path: '/channels', desc: '配置飞书/企业微信/微信渠道' },
  { label: 'MCP 管理', path: '/mcp', desc: '管理 MCP 工具服务器' },
  { label: 'Agent 管理', path: '/agents', desc: '配置 AI Agent 角色和工具' },
]

export default function ConfigPage() {
  const navigate = useNavigate()
  const [config, setConfig] = useState<SystemConfigDto | null>(null)
  const [agentDepth, setAgentDepth] = useState(3)
  const [folders, setFolders] = useState<string[]>([])
  const [newFolder, setNewFolder] = useState('')
  const [savingAgent, setSavingAgent] = useState(false)
  const [savingSkills, setSavingSkills] = useState(false)

  const load = useCallback(async () => {
    try {
      const data = await getSystemConfig()
      setConfig(data)
      setAgentDepth(data.agent.subAgentMaxDepth)
      setFolders(data.skills.additionalFolders)
    } catch {
      toaster.create({ type: 'error', title: '加载配置失败' })
    }
  }, [])

  useEffect(() => { load() }, [load])

  const handleSaveAgent = async () => {
    if (agentDepth < 1 || agentDepth > 10) {
      toaster.create({ type: 'error', title: '嵌套深度必须在 1–10 之间' })
      return
    }
    setSavingAgent(true)
    try {
      await updateAgentConfig({ subAgentMaxDepth: agentDepth })
      toaster.create({ type: 'success', title: '已保存，需重启生效' })
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSavingAgent(false)
    }
  }

  const handleAddFolder = () => {
    const trimmed = newFolder.trim()
    if (!trimmed) return
    if (folders.includes(trimmed)) {
      toaster.create({ type: 'error', title: '该路径已存在' })
      return
    }
    setFolders([...folders, trimmed])
    setNewFolder('')
  }

  const handleRemoveFolder = (index: number) => {
    setFolders(folders.filter((_, i) => i !== index))
  }

  const handleSaveSkills = async () => {
    setSavingSkills(true)
    try {
      await updateSkillsConfig({ additionalFolders: folders })
      toaster.create({ type: 'success', title: '已保存，需重启生效' })
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSavingSkills(false)
    }
  }

  return (
    <Box p="6" maxW="900px">
      <Text fontSize="xl" fontWeight="bold" mb="6">系统配置</Text>

      {/* Editable config cards */}
      {config && (
        <SimpleGrid columns={[1, 2]} gap="6" mb="8">
          {/* Agent config */}
          <Box p="5" borderWidth="1px" borderRadius="lg">
            <Flex justify="space-between" align="center" mb="3">
              <Text fontWeight="semibold">Agent 设置</Text>
              <Badge colorPalette="orange" size="sm">需重启生效</Badge>
            </Flex>
            <Text fontSize="sm" color="gray.500" mb="3">
              子代理最大嵌套深度（1–10，默认 3）
            </Text>
            <Flex gap="3" align="center">
              <Input
                type="number"
                value={agentDepth}
                onChange={(e) => setAgentDepth(Number(e.target.value))}
                min={1}
                max={10}
                w="80px"
              />
              <Button
                size="sm"
                colorPalette="blue"
                onClick={handleSaveAgent}
                loading={savingAgent}
              >
                保存
              </Button>
            </Flex>
          </Box>

          {/* Skills config */}
          <Box p="5" borderWidth="1px" borderRadius="lg">
            <Flex justify="space-between" align="center" mb="3">
              <Text fontWeight="semibold">技能设置</Text>
              <Badge colorPalette="orange" size="sm">需重启生效</Badge>
            </Flex>
            <Text fontSize="sm" color="gray.500" mb="3">
              附加技能文件夹（只读扫描源，不会写入新技能）
            </Text>
            <Stack gap="2" mb="3">
              {folders.map((folder, i) => (
                <Flex key={i} gap="2" align="center">
                  <Text fontSize="sm" fontFamily="mono" flex="1" truncate>{folder}</Text>
                  <IconButton
                    aria-label="移除"
                    size="xs"
                    variant="ghost"
                    colorPalette="red"
                    onClick={() => handleRemoveFolder(i)}
                  >
                    <Trash2 size={14} />
                  </IconButton>
                </Flex>
              ))}
            </Stack>
            <Flex gap="2">
              <Input
                size="sm"
                placeholder="输入文件夹路径"
                value={newFolder}
                onChange={(e) => setNewFolder(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleAddFolder()}
                flex="1"
              />
              <IconButton
                aria-label="添加"
                size="sm"
                variant="outline"
                onClick={handleAddFolder}
              >
                <Plus size={16} />
              </IconButton>
            </Flex>
            <Flex justify="flex-end" mt="3">
              <Button
                size="sm"
                colorPalette="blue"
                onClick={handleSaveSkills}
                loading={savingSkills}
              >
                保存
              </Button>
            </Flex>
          </Box>
        </SimpleGrid>
      )}

      {/* Quick nav */}
      <Text fontWeight="semibold" mb="3">快捷导航</Text>
      <SimpleGrid columns={[2, 4]} gap="4" mb="8">
        {QUICK_LINKS.map((link) => (
          <Box
            key={link.path}
            p="4"
            borderWidth="1px"
            borderRadius="lg"
            cursor="pointer"
            _hover={{ bg: 'gray.50', _dark: { bg: 'gray.700' } }}
            onClick={() => navigate(link.path)}
          >
            <Text fontWeight="semibold" fontSize="sm">{link.label}</Text>
            <Text fontSize="xs" color="gray.500" mt="1">{link.desc}</Text>
          </Box>
        ))}
      </SimpleGrid>

      {/* Config reference */}
      <Text fontWeight="semibold" mb="3">配置项参考</Text>
      <Badge colorPalette="blue" mb="3">只读参考 — 通过环境变量或配置文件设置</Badge>
      <Table.Root variant="outline" size="sm">
        <Table.Header>
          <Table.Row>
            <Table.ColumnHeader>配置项</Table.ColumnHeader>
            <Table.ColumnHeader>说明</Table.ColumnHeader>
            <Table.ColumnHeader>默认值</Table.ColumnHeader>
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {CONFIG_REFERENCE.map((row) => (
            <Table.Row key={row.key}>
              <Table.Cell fontFamily="mono" fontSize="xs">{row.key}</Table.Cell>
              <Table.Cell fontSize="sm">{row.desc}</Table.Cell>
              <Table.Cell fontSize="xs" color="gray.500">{row.default || '—'}</Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table.Root>
    </Box>
  )
}
