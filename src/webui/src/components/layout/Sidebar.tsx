import { Box, Flex, Text, Tooltip } from '@chakra-ui/react'
// Tooltip is used as compound component: Tooltip.Root / Tooltip.Trigger / Tooltip.Content
import { useLocation, useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, ChevronDown, ChevronUp } from 'lucide-react'
import { useUIStore } from '@/store/uiStore'
import { MENU_GROUPS } from '@/config/navigation'

export default function Sidebar() {
  const { sidebarCollapsed, toggleSidebar, collapsedGroups, toggleGroup } = useUIStore()
  const location = useLocation()
  const navigate = useNavigate()

  const isActive = (path: string) => location.pathname === path

  return (
    <Box
      as="nav"
      w={sidebarCollapsed ? '52px' : '200px'}
      minW={sidebarCollapsed ? '52px' : '200px'}
      h="100%"
      bg="var(--mc-card)"
      borderRightWidth="1px"
      borderColor="var(--mc-border)"
      py="2"
      display="flex"
      flexDir="column"
      transition="width 0.2s, min-width 0.2s"
      overflow="hidden"
    >
      {/* Nav groups */}
      <Box flex="1" overflowY="auto" overflowX="hidden">
        {MENU_GROUPS.map((group) => {
          const isGroupCollapsed = collapsedGroups.includes(group.title)
          return (
          <Box key={group.title} mb="2">
            {!sidebarCollapsed && (
              <Flex
                align="center"
                justify="space-between"
                px="3"
                py="1"
                cursor="pointer"
                userSelect="none"
                _hover={{ bg: 'var(--mc-card-hover, var(--mc-card))' }}
                borderRadius="sm"
                onClick={() => toggleGroup(group.title)}
              >
                <Text
                  fontSize="xs"
                  fontWeight="semibold"
                  color="var(--mc-text-muted)"
                  textTransform="uppercase"
                  letterSpacing="wider"
                  whiteSpace="nowrap"
                >
                  {group.title}
                </Text>
                <Box color="var(--mc-text-muted)" flexShrink={0}>
                  {isGroupCollapsed ? <ChevronDown size={12} /> : <ChevronUp size={12} />}
                </Box>
              </Flex>
            )}
            {sidebarCollapsed && <Box h="2" />}
            {!isGroupCollapsed && group.items.map((item) => (
              <Tooltip.Root
                key={item.path}
                positioning={{ placement: 'right' }}
                disabled={!sidebarCollapsed}
              >
                <Tooltip.Trigger asChild>
                  <Flex
                    align="center"
                    gap="2"
                    px={sidebarCollapsed ? '0' : '3'}
                    py="2"
                    mx="2"
                    borderRadius="md"
                    cursor="pointer"
                    justify={sidebarCollapsed ? 'center' : 'flex-start'}
                    bg={isActive(item.path) ? 'var(--mc-sidebar-active)' : 'transparent'}
                    color={isActive(item.path) ? 'var(--mc-primary)' : 'var(--mc-text)'}
                    borderLeftWidth={isActive(item.path) ? '2px' : '2px'}
                    borderLeftColor={isActive(item.path) ? 'var(--mc-sidebar-active-border)' : 'transparent'}
                    _hover={{
                      bg: isActive(item.path) ? 'var(--mc-sidebar-active)' : 'var(--mc-card-hover, var(--mc-card))',
                    }}
                    onClick={() => navigate(item.path)}
                    transition="background 0.15s"
                  >
                    <Box flexShrink={0}>{item.icon}</Box>
                    {!sidebarCollapsed && (
                      <Text fontSize="sm" whiteSpace="nowrap" overflow="hidden" textOverflow="ellipsis">
                        {item.label}
                      </Text>
                    )}
                  </Flex>
                </Tooltip.Trigger>
                <Tooltip.Content>{item.label}</Tooltip.Content>
              </Tooltip.Root>
            ))}
          </Box>
          )
        })}
      </Box>

      {/* Collapse toggle */}
      <Flex
        align="center"
        justify={sidebarCollapsed ? 'center' : 'flex-end'}
        px="2"
        py="2"
        mt="auto"
        borderTopWidth="1px"
        borderColor="var(--mc-border)"
      >
        <Box
          as="button"
          p="1"
          borderRadius="md"
          cursor="pointer"
          color="var(--mc-text-muted)"
          _hover={{ bg: 'var(--mc-card-hover, var(--mc-card))' }}
          onClick={toggleSidebar}
        >
          {sidebarCollapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
        </Box>
      </Flex>
    </Box>
  )
}
