import { Box, Text, Badge, SimpleGrid, Table } from '@chakra-ui/react'
import { useNavigate } from 'react-router-dom'

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
  return (
    <Box p="6" maxW="900px">
      <Text fontSize="xl" fontWeight="bold" mb="6">系统配置</Text>

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
