import { Box, Flex, Text, Badge, Button, HStack, VStack, Spinner, Card, Portal, Tooltip } from '@chakra-ui/react'
import { Edit, Trash2, ChevronDown, ChevronRight, Puzzle, CheckCircle, XCircle } from 'lucide-react'
import type { McpServerConfig, McpToolInfo, McpEnvVarInfo } from '@/api/gateway'

function transportLabel(t: string): string {
  return { stdio: 'Stdio', sse: 'SSE', http: 'HTTP' }[t] ?? t
}

// ─── 环境变量状态 Pills ────────────────────────────────────────────────────────

function EnvVarPills({ vars }: { vars: McpEnvVarInfo[] }) {
  if (vars.length === 0) return null
  return (
    <Flex gap="1.5" flexWrap="wrap" mt="2">
      {vars.map((v) => (
        <HStack
          key={v.name}
          gap="1"
          px="2"
          py="0.5"
          borderRadius="full"
          borderWidth="1px"
          borderColor={v.isSet ? 'green.300' : 'orange.300'}
          bg={v.isSet ? 'green.50' : 'orange.50'}
         
          fontSize="xs"
        >
          {v.isSet
            ? <CheckCircle size={10} color="var(--chakra-colors-green-500)" />
            : <XCircle size={10} color="var(--chakra-colors-orange-500)" />}
          <Text fontFamily="mono" color={v.isSet ? 'green.700' : 'orange.700'}>
            ${`{${v.name}}`}
          </Text>
        </HStack>
      ))}
    </Flex>
  )
}

// ─── MCP 服务器卡片 ────────────────────────────────────────────────────────────

export interface McpServerCardProps {
  server: McpServerConfig
  expanded: boolean
  tools: McpToolInfo[] | undefined
  toolsLoading: boolean
  onExpand: (id: string) => void
  onEdit: (s: McpServerConfig) => void
  onDelete: (s: McpServerConfig) => void
}

export function McpServerCard({ server: s, expanded, tools, toolsLoading, onExpand, onEdit, onDelete }: McpServerCardProps) {
  const requiredEnvVars = s.requiredEnvVars ?? []
  const hasMissing = requiredEnvVars.some((v) => !v.isSet)

  const borderColor = hasMissing
    ? 'orange.400'
    : s.isEnabled
      ? 'green.400'
      : 'gray.300'

  const cmdOrUrl = s.transportType === 'stdio'
    ? [s.command, ...(s.args ?? [])].filter(Boolean).join(' ')
    : s.url ?? ''

  return (
    <Card.Root
      variant="outline"
      borderLeftWidth="3px"
      borderLeftColor={borderColor}
      opacity={s.isEnabled ? 1 : 0.65}
    >
      <Card.Body p="3">
        {/* Header row */}
        <Flex align="center" gap="2">
          {/* Status dot */}
          <Box
            w="7px" h="7px" rounded="full" flexShrink={0}
            bg={s.isEnabled ? 'green.400' : 'gray.300'}
          />

          {/* Name */}
          <Text fontWeight="semibold" fontSize="sm" flex="1" truncate>{s.name}</Text>

          {/* Transport badge */}
          <Badge size="sm" variant="subtle">{transportLabel(s.transportType)}</Badge>

          {/* Source badge */}
          {s.source === 'plugin'
            ? (
              <Badge size="sm" colorPalette="blue" variant="subtle">
                <Puzzle size={10} />
                插件: {s.pluginName ?? s.pluginId}
              </Badge>
            )
            : <Badge size="sm" colorPalette="gray" variant="subtle">手动</Badge>}

          {/* Actions */}
          <HStack gap="0.5" ml="1">
            <Button size="xs" variant="ghost" onClick={() => onEdit(s)}>
              <Edit size={12} />
            </Button>
            {s.source === 'plugin'
              ? (
                <Tooltip.Root>
                  <Tooltip.Trigger asChild>
                    <Button size="xs" variant="ghost" colorPalette="gray" disabled>
                      <Trash2 size={12} />
                    </Button>
                  </Tooltip.Trigger>
                  <Portal>
                    <Tooltip.Positioner>
                      <Tooltip.Content>通过卸载插件删除</Tooltip.Content>
                    </Tooltip.Positioner>
                  </Portal>
                </Tooltip.Root>
              )
              : (
                <Button size="xs" variant="ghost" colorPalette="red" onClick={() => onDelete(s)}>
                  <Trash2 size={12} />
                </Button>
              )}
          </HStack>
        </Flex>

        {/* Command / URL line */}
        {cmdOrUrl && (
          <Text fontSize="xs" fontFamily="mono" color="var(--mc-text-muted)" mt="1.5" truncate title={cmdOrUrl}>
            {cmdOrUrl}
          </Text>
        )}

        {/* Required env vars pills */}
        {requiredEnvVars.length > 0 && <EnvVarPills vars={requiredEnvVars} />}

        {/* Tools expander */}
        <Flex
          align="center"
          gap="1"
          mt="2"
          cursor="pointer"
          color="var(--mc-text-muted)"
          _hover={{ color: 'gray.700' }}
          onClick={() => onExpand(s.id)}
          userSelect="none"
          w="fit-content"
        >
          {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          <Text fontSize="xs">工具</Text>
        </Flex>

        {/* Tools list */}
        {expanded && (
          <Box mt="1.5" pl="3" borderLeftWidth="2px" borderLeftColor="var(--mc-border)">
            {toolsLoading && <Spinner size="sm" />}
            {!toolsLoading && (tools ?? []).length === 0 && (
              <Text fontSize="xs" color="var(--mc-text-muted)">无工具（或尚未加载）</Text>
            )}
            {!toolsLoading && (tools ?? []).map((t) => (
              <HStack key={t.name} py="0.5">
                <Text fontSize="xs" fontWeight="medium" w="160px" truncate>{t.name}</Text>
                <Text fontSize="xs" color="var(--mc-text-muted)" truncate>{t.description}</Text>
              </HStack>
            ))}
          </Box>
        )}
      </Card.Body>
    </Card.Root>
  )
}
