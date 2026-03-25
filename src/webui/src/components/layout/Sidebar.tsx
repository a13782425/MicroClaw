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
      bg="gray.50"
      _dark={{ bg: 'gray.900' }}
      borderRightWidth="1px"
      borderColor="gray.200"
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
                _hover={{ bg: 'gray.100', _dark: { bg: 'gray.800' } }}
                borderRadius="sm"
                onClick={() => toggleGroup(group.title)}
              >
                <Text
                  fontSize="xs"
                  fontWeight="semibold"
                  color="gray.400"
                  textTransform="uppercase"
                  letterSpacing="wider"
                  whiteSpace="nowrap"
                >
                  {group.title}
                </Text>
                <Box color="gray.400" flexShrink={0}>
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
                    bg={isActive(item.path) ? 'blue.50' : 'transparent'}
                    color={isActive(item.path) ? 'blue.600' : 'gray.700'}
                    _hover={{
                      bg: isActive(item.path) ? 'blue.50' : 'gray.100',
                      _dark: { bg: isActive(item.path) ? 'blue.900' : 'gray.700' }
                    }}
                    _dark={{
                      color: isActive(item.path) ? 'blue.300' : 'gray.300',
                      bg: isActive(item.path) ? 'blue.900' : 'transparent',
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
        borderColor="gray.200"
      >
        <Box
          as="button"
          p="1"
          borderRadius="md"
          cursor="pointer"
          _hover={{ bg: 'gray.200', _dark: { bg: 'gray.700' } }}
          onClick={toggleSidebar}
        >
          {sidebarCollapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
        </Box>
      </Flex>
    </Box>
  )
}
