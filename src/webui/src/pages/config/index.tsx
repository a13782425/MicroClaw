import { useState, useEffect, useCallback } from 'react'
import { Box, Text, Badge, SimpleGrid, Table, Input, Button, Flex, IconButton, Stack, Accordion } from '@chakra-ui/react'
import { useNavigate } from 'react-router-dom'
import { Plus, Trash2 } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import {
  getSystemConfig,
  updateAgentConfig,
  updateSkillsConfig,
  updateEmotionConfig,
  type SystemConfigDto,
  type EmotionConfigSection,
  type BehaviorProfileConfigSection,
  type EmotionDeltaConfigSection,
} from '@/api/gateway'

/**
 * 系统配置页面（Config）
 * 左侧分类导航 + 右侧内容面板布局
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

const SECTIONS = [
  { key: 'agent', label: 'Agent 设置' },
  { key: 'skills', label: '技能设置' },
  { key: 'emotion', label: '情绪行为' },
] as const
type SectionKey = (typeof SECTIONS)[number]['key']

const BEHAVIOR_MODES: { key: 'normal' | 'explore' | 'cautious' | 'rest'; label: string; color: string }[] = [
  { key: 'normal', label: '正常模式', color: 'blue' },
  { key: 'explore', label: '探索模式', color: 'green' },
  { key: 'cautious', label: '谨慎模式', color: 'orange' },
  { key: 'rest', label: '休息模式', color: 'purple' },
]

type EventDeltaKey = 'deltaMessageSuccess' | 'deltaMessageFailed' | 'deltaToolSuccess' | 'deltaToolError' |
  'deltaUserSatisfied' | 'deltaUserDissatisfied' | 'deltaTaskCompleted' | 'deltaTaskFailed' |
  'deltaPainHigh' | 'deltaPainCritical'

const EVENT_DELTA_DEFS: { key: EventDeltaKey; label: string; color: string; desc: string }[] = [
  { key: 'deltaMessageSuccess',   label: '消息发送成功', color: 'green',  desc: '默认：心情+3, 信心+2' },
  { key: 'deltaMessageFailed',    label: '消息发送失败', color: 'red',    desc: '默认：警觉+8, 心情-5, 信心-5' },
  { key: 'deltaToolSuccess',      label: 'Tool 执行成功', color: 'teal',   desc: '默认：好奇心+2, 信心+3' },
  { key: 'deltaToolError',        label: 'Tool 执行报错', color: 'red',    desc: '默认：警觉+10, 心情-3, 信心-5' },
  { key: 'deltaUserSatisfied',    label: '用户满意',    color: 'green',  desc: '默认：心情+10, 信心+5' },
  { key: 'deltaUserDissatisfied', label: '用户不满意',   color: 'red',    desc: '默认：心情-10, 信心-5, 警觉+5' },
  { key: 'deltaTaskCompleted',    label: '任务完成',    color: 'blue',   desc: '默认：心情+8, 信心+8, 警觉-5' },
  { key: 'deltaTaskFailed',       label: '任务失败',    color: 'orange', desc: '默认：警觉+10, 心情-8, 信心-8' },
  { key: 'deltaPainHigh',         label: '高严重度痛觉',  color: 'orange', desc: '默认：警觉+22, 心情-5, 信心-18' },
  { key: 'deltaPainCritical',     label: '极高严重度痛觉', color: 'red',    desc: '默认：警觉+32, 心情-10, 信心-28' },
]

// ── Agent Panel ───────────────────────────────────────────────────────────────

function AgentPanel({ config, navigate }: { config: SystemConfigDto; navigate: (path: string) => void }) {
  const [agentDepth, setAgentDepth] = useState(config.agent.subAgentMaxDepth)
  const [saving, setSaving] = useState(false)

  const handleSave = async () => {
    if (agentDepth < 1 || agentDepth > 10) {
      toaster.create({ type: 'error', title: '嵌套深度必须在 1–10 之间' })
      return
    }
    setSaving(true)
    try {
      await updateAgentConfig({ subAgentMaxDepth: agentDepth })
      toaster.create({ type: 'success', title: '已保存，需重启生效' })
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Box>
      <Text fontWeight="semibold" fontSize="lg" mb="4">Agent 设置</Text>
      <Box p="5" borderWidth="1px" borderRadius="lg" mb="6">
        <Flex justify="space-between" align="center" mb="3">
          <Text fontWeight="semibold">子代理配置</Text>
          <Badge colorPalette="orange" size="sm">需重启生效</Badge>
        </Flex>
        <Text fontSize="sm" color="var(--mc-text-muted)" mb="3">子代理最大嵌套深度（1–10，默认 3）</Text>
        <Flex gap="3" align="center">
          <Input
            type="number"
            value={agentDepth}
            onChange={(e) => setAgentDepth(Number(e.target.value))}
            min={1}
            max={10}
            w="80px"
          />
          <Button size="sm" colorPalette="blue" onClick={handleSave} loading={saving}>保存</Button>
        </Flex>
      </Box>

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
            <Text fontSize="xs" color="var(--mc-text-muted)" mt="1">{link.desc}</Text>
          </Box>
        ))}
      </SimpleGrid>

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
              <Table.Cell fontSize="xs" color="var(--mc-text-muted)">{row.default || '—'}</Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table.Root>
    </Box>
  )
}

// ── Skills Panel ──────────────────────────────────────────────────────────────

function SkillsPanel({ config }: { config: SystemConfigDto }) {
  const [folders, setFolders] = useState<string[]>(config.skills.additionalFolders)
  const [newFolder, setNewFolder] = useState('')
  const [saving, setSaving] = useState(false)

  const handleAdd = () => {
    const trimmed = newFolder.trim()
    if (!trimmed) return
    if (folders.includes(trimmed)) {
      toaster.create({ type: 'error', title: '该路径已存在' })
      return
    }
    setFolders([...folders, trimmed])
    setNewFolder('')
  }

  const handleSave = async () => {
    setSaving(true)
    try {
      await updateSkillsConfig({ additionalFolders: folders })
      toaster.create({ type: 'success', title: '已保存，需重启生效' })
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Box>
      <Text fontWeight="semibold" fontSize="lg" mb="4">技能设置</Text>
      <Box p="5" borderWidth="1px" borderRadius="lg">
        <Flex justify="space-between" align="center" mb="3">
          <Text fontWeight="semibold">附加技能文件夹</Text>
          <Badge colorPalette="orange" size="sm">需重启生效</Badge>
        </Flex>
        <Text fontSize="sm" color="var(--mc-text-muted)" mb="3">附加技能文件夹（只读扫描源，不会写入新技能）</Text>
        <Stack gap="2" mb="3">
          {folders.map((folder, i) => (
            <Flex key={i} gap="2" align="center">
              <Text fontSize="sm" fontFamily="mono" flex="1" truncate>{folder}</Text>
              <IconButton
                aria-label="移除"
                size="xs"
                variant="ghost"
                colorPalette="red"
                onClick={() => setFolders(folders.filter((_, idx) => idx !== i))}
              >
                <Trash2 size={14} />
              </IconButton>
            </Flex>
          ))}
        </Stack>
        <Flex gap="2" mb="3">
          <Input
            size="sm"
            placeholder="输入文件夹路径"
            value={newFolder}
            onChange={(e) => setNewFolder(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleAdd()}
            flex="1"
          />
          <IconButton aria-label="添加" size="sm" variant="outline" onClick={handleAdd}>
            <Plus size={16} />
          </IconButton>
        </Flex>
        <Flex justify="flex-end">
          <Button size="sm" colorPalette="blue" onClick={handleSave} loading={saving}>保存</Button>
        </Flex>
      </Box>
    </Box>
  )
}

// ── Behavior Profile Card ─────────────────────────────────────────────────────

function BehaviorProfileCard({
  label,
  colorPalette,
  value,
  onChange,
}: {
  label: string
  colorPalette: string
  value: BehaviorProfileConfigSection
  onChange: (v: BehaviorProfileConfigSection) => void
}) {
  return (
    <Box p="4" borderWidth="1px" borderRadius="lg">
      <Badge colorPalette={colorPalette} size="sm" mb="3">{label}</Badge>
      <Stack gap="3">
        <Flex align="center" gap="3">
          <Text fontSize="sm" w="120px" flexShrink={0}>Temperature</Text>
          <Input
            size="sm"
            type="number"
            step="0.1"
            min={0}
            max={2}
            value={value.temperature ?? ''}
            onChange={(e) => onChange({ ...value, temperature: e.target.value === '' ? undefined : parseFloat(e.target.value) })}
            w="90px"
          />
          <Text fontSize="xs" color="var(--mc-text-muted)">0.0 – 2.0</Text>
        </Flex>
        <Flex align="center" gap="3">
          <Text fontSize="sm" w="120px" flexShrink={0}>Top P</Text>
          <Input
            size="sm"
            type="number"
            step="0.05"
            min={0.01}
            max={1}
            value={value.topP ?? ''}
            onChange={(e) => onChange({ ...value, topP: e.target.value === '' ? undefined : parseFloat(e.target.value) })}
            w="90px"
          />
          <Text fontSize="xs" color="var(--mc-text-muted)">(0, 1]</Text>
        </Flex>
        <Flex align="center" gap="3">
          <Text fontSize="sm" w="120px" flexShrink={0}>提示后缀</Text>
          <Input
            size="sm"
            value={value.systemPromptSuffix ?? ''}
            onChange={(e) => onChange({ ...value, systemPromptSuffix: e.target.value })}
            placeholder="追加到 System Prompt 末尾（可为空）"
            flex="1"
          />
        </Flex>
      </Stack>
    </Box>
  )
}

// ── Emotion Panel ─────────────────────────────────────────────────────────────

function EmotionPanel({ config }: { config: SystemConfigDto }) {
  const [emotion, setEmotion] = useState<EmotionConfigSection>(config.emotion)
  const [saving, setSaving] = useState(false)

  const setThreshold = (key: string, val: number) =>
    setEmotion((prev) => ({ ...prev, [key]: val }))

  const setProfile = (mode: 'normal' | 'explore' | 'cautious' | 'rest', val: BehaviorProfileConfigSection) =>
    setEmotion((prev) => ({ ...prev, [mode]: val }))

  const setDelta = (key: EventDeltaKey, val: EmotionDeltaConfigSection) =>
    setEmotion((prev) => ({ ...prev, [key]: val }))

  const handleSave = async () => {
    setSaving(true)
    try {
      await updateEmotionConfig(emotion)
      toaster.create({ type: 'success', title: '已保存，需重启生效' })
    } catch (err) {
      toaster.create({ type: 'error', title: '保存失败', description: String(err) })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Box>
      <Text fontWeight="semibold" fontSize="lg" mb="4">情绪行为</Text>

      <Box p="5" borderWidth="1px" borderRadius="lg" mb="5">
        <Flex justify="space-between" align="center" mb="4">
          <Text fontWeight="semibold">模式切换阈值</Text>
          <Badge colorPalette="orange" size="sm">需重启生效</Badge>
        </Flex>
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="4">
          情绪值域 [0, 100]，判定优先级：谨慎 &gt; 探索 &gt; 休息 &gt; 正常
        </Text>
        <SimpleGrid columns={[1, 2]} gap="3">
          {[
            { key: 'cautiousAlertnessThreshold', label: '谨慎：警觉度 ≥', color: 'orange' },
            { key: 'cautiousConfidenceThreshold', label: '谨慎：信心 ≤', color: 'orange' },
            { key: 'exploreMinCuriosity', label: '探索：好奇心 ≥', color: 'green' },
            { key: 'exploreMinMood', label: '探索：心情 ≥', color: 'green' },
            { key: 'restMaxAlertness', label: '休息：警觉度 ≤', color: 'purple' },
            { key: 'restMaxMood', label: '休息：心情 ≤', color: 'purple' },
          ].map(({ key, label, color }) => (
            <Flex key={key} align="center" gap="3">
              <Flex align="center" gap="2" w="150px" flexShrink={0}>
                <Box w="9px" h="9px" borderRadius="full" bg={`${color}.500`} flexShrink={0} />
                <Text fontSize="md" color="var(--mc-text)">{label}</Text>
              </Flex>
              <Input
                size="sm"
                type="number"
                min={0}
                max={100}
                value={(emotion as Record<string, unknown>)[key] as number ?? 0}
                onChange={(e) => setThreshold(key, parseInt(e.target.value) || 0)}
                w="70px"
              />
            </Flex>
          ))}
        </SimpleGrid>
      </Box>

      <Text fontWeight="semibold" mb="3">行为模式推理参数</Text>
      <SimpleGrid columns={[1, 2]} gap="4" mb="5">
        {BEHAVIOR_MODES.map(({ key, label, color }) => (
          <BehaviorProfileCard
            key={key}
            label={label}
            colorPalette={color}
            value={emotion[key] as BehaviorProfileConfigSection}
            onChange={(val) => setProfile(key, val)}
          />
        ))}
      </SimpleGrid>

      <Text fontWeight="semibold" mb="3">事件加减分</Text>
      <Text fontSize="xs" color="var(--mc-text-muted)" mb="3">调整各事件触发时四个情绪维度的变化量（正数加分、负数减分、空表示不变）</Text>
      <Accordion.Root multiple mb="5">
        {EVENT_DELTA_DEFS.map(({ key, label, color, desc }) => {
          const delta = emotion[key] as EmotionDeltaConfigSection
          return (
            <Accordion.Item key={key} value={key} borderWidth="1px" borderRadius="lg" mb="2">
              <Accordion.ItemTrigger px="4" py="3">
                <Flex align="center" gap="2" flex="1">
                  <Box w="8px" h="8px" borderRadius="full" bg={`${color}.500`} flexShrink={0} />
                  <Text fontWeight="medium" fontSize="sm">{label}</Text>
                  <Text fontSize="xs" color="var(--mc-text-muted)" ml="2">{desc}</Text>
                </Flex>
                <Accordion.ItemIndicator />
              </Accordion.ItemTrigger>
              <Accordion.ItemContent px="4" pb="4">
                <SimpleGrid columns={2} gap="3">
                  {([
                    { field: 'alertness',  label: '警觉度' },
                    { field: 'mood',       label: '心情' },
                    { field: 'curiosity',  label: '好奇心' },
                    { field: 'confidence', label: '信心' },
                  ] as { field: keyof EmotionDeltaConfigSection; label: string }[]).map(({ field, label: fl }) => (
                    <Flex key={field} align="center" gap="2">
                      <Text fontSize="sm" w="52px" flexShrink={0}>{fl}</Text>
                      <Input
                        size="sm"
                        type="number"
                        min={-100}
                        max={100}
                        w="72px"
                        value={delta[field] ?? ''}
                        placeholder="0"
                        onChange={(e) => setDelta(key, {
                          ...delta,
                          [field]: e.target.value === '' ? undefined : parseInt(e.target.value),
                        })}
                      />
                    </Flex>
                  ))}
                </SimpleGrid>
              </Accordion.ItemContent>
            </Accordion.Item>
          )
        })}
      </Accordion.Root>

      <Flex justify="flex-end">
        <Button colorPalette="blue" onClick={handleSave} loading={saving}>保存全部</Button>
      </Flex>
    </Box>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function ConfigPage() {
  const navigate = useNavigate()
  const [config, setConfig] = useState<SystemConfigDto | null>(null)
  const [activeSection, setActiveSection] = useState<SectionKey>('agent')

  const load = useCallback(async () => {
    try {
      const data = await getSystemConfig()
      setConfig(data)
    } catch {
      toaster.create({ type: 'error', title: '加载配置失败' })
    }
  }, [])

  useEffect(() => { load() }, [load])

  if (!config) {
    return <Box p="6"><Text color="var(--mc-text-muted)">加载中…</Text></Box>
  }

  return (
    <Flex h="full" overflow="hidden">
      {/* 左侧分类导航 */}
      <Box w="180px" flexShrink={0} borderRightWidth="1px" py="4" px="2" overflowY="auto">
        <Text fontSize="xs" color="var(--mc-text-muted)" fontWeight="semibold" px="3" mb="2">系统配置</Text>
        <Stack gap="1">
          {SECTIONS.map((sec) => (
            <Box
              key={sec.key}
              px="3"
              py="2"
              borderRadius="md"
              cursor="pointer"
              fontSize="sm"
              fontWeight={activeSection === sec.key ? 'semibold' : 'normal'}
              bg={activeSection === sec.key ? 'blue.500' : 'transparent'}
              color={activeSection === sec.key ? 'white' : undefined}
              _hover={activeSection === sec.key ? {} : { bg: 'gray.100', _dark: { bg: 'gray.700' } }}
              onClick={() => setActiveSection(sec.key)}
            >
              {sec.label}
            </Box>
          ))}
        </Stack>
      </Box>

      {/* 右侧内容面板 */}
      <Box flex="1" overflowY="auto" p="6" maxW="800px">
        {activeSection === 'agent' && <AgentPanel config={config} navigate={navigate} />}
        {activeSection === 'skills' && <SkillsPanel config={config} />}
        {activeSection === 'emotion' && <EmotionPanel config={config} />}
      </Box>
    </Flex>
  )
}
