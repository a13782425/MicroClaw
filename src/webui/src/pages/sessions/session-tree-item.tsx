import { useState } from 'react'
import {
  Box, Flex, Text, IconButton, Badge, Spinner,
} from '@chakra-ui/react'
import { MessageCircle, Trash2, ChevronRight, ChevronDown } from 'lucide-react'
import type { SessionTreeNode } from '@/store/sessionStore'

const CHANNEL_LABELS: Record<string, string> = {
  feishu: '飞书', wecom: '企微', wechat: '微信', web: 'Web',
}

function channelBadge(type: string) {
  const colors: Record<string, string> = {
    feishu: 'cyan', wecom: 'green', wechat: 'teal', web: 'gray',
  }
  if (type === 'web') return null
  return (
    <Badge size="sm" colorPalette={colors[type] ?? 'gray'} ml="1">
      {CHANNEL_LABELS[type] ?? type}
    </Badge>
  )
}

interface SessionTreeItemProps {
  node: SessionTreeNode
  depth: number
  activeId: string | null
  runningSessionIds: ReadonlySet<string>
  onSelect: (id: string) => void
  onDelete: (id: string) => void
}

export default function SessionTreeItem({
  node, depth, activeId, runningSessionIds, onSelect, onDelete,
}: SessionTreeItemProps) {
  const [expanded, setExpanded] = useState(false)
  const { session } = node
  const hasChildren = node.children.length > 0
  const isActive = session.id === activeId
  const isRunning = runningSessionIds.has(session.id)

  return (
    <>
      <Flex
        px="3" py="2.5"
        pl={`${12 + depth * 16}px`}
        align="center"
        cursor="pointer"
        bg={isActive ? 'blue.50' : undefined}
       
        _hover={{ bg: isActive ? 'blue.50' : 'gray.50', _dark: { bg: isActive ? 'blue.900' : 'gray.700' } }}
        onClick={() => onSelect(session.id)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            onSelect(session.id)
          }
        }}
        role="button"
        tabIndex={0}
        className="group"
        _focusVisible={{ outline: '2px solid', outlineColor: 'blue.400', outlineOffset: '-2px' }}
      >
        {/* 展开/折叠箭头 */}
        {hasChildren ? (
          <Box
            mr="1" flexShrink={0} cursor="pointer"
            color="var(--mc-text-muted)"
            onClick={(e) => { e.stopPropagation(); setExpanded((v) => !v) }}
          >
            {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </Box>
        ) : (
          <Box mr="1" w="14px" flexShrink={0} />
        )}

        {/* 图标 */}
        <Box color={isActive ? 'blue.500' : 'gray.400'} mr="2" flexShrink={0}>
          {isRunning && !isActive
            ? <Spinner size="xs" color="var(--mc-info)" />
            : <MessageCircle size={14} />}
        </Box>

        {/* 标题 + badges */}
        <Box flex="1" minW="0">
          <Text
            fontSize="sm"
            fontWeight={isActive ? 'medium' : 'normal'}
            color={isActive ? 'blue.600' : undefined}
           
            truncate
          >
            {session.title}
          </Text>
          <Flex gap="1" mt="0.5">
            {channelBadge(session.channelType)}
            {session.agentId && (
              <Badge size="sm" colorPalette="orange">Agent</Badge>
            )}
          </Flex>
        </Box>

        {/* 删除按钮 */}
        <IconButton
          size="xs" variant="ghost" colorPalette="red"
          aria-label="删除会话"
          opacity={isActive ? 1 : 0}
          _groupHover={{ opacity: 1 }}
          _groupFocusWithin={{ opacity: 1 }}
          onClick={(e) => { e.stopPropagation(); onDelete(session.id) }}
        >
          <Trash2 size={12} />
        </IconButton>
      </Flex>

      {/* 递归渲染子节点 */}
      {hasChildren && expanded && node.children.map((child) => (
        <SessionTreeItem
          key={child.session.id}
          node={child}
          depth={depth + 1}
          activeId={activeId}
          runningSessionIds={runningSessionIds}
          onSelect={onSelect}
          onDelete={onDelete}
        />
      ))}
    </>
  )
}
