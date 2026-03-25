import { useState, useEffect } from 'react'
import { Box, Flex, Text, Badge } from '@chakra-ui/react'
import { Cpu, LogOut, Sun, Moon } from 'lucide-react'
import { useAuthStore } from '@/store/authStore'
import { signalRService } from '@/services/signalr'
import { getGatewayHealth } from '@/api/gateway'
import { useNavigate } from 'react-router-dom'
import { useColorMode } from '@/components/ui/color-mode'

interface HeaderProps {
  onMenuToggle: () => void
}

export default function Header({ onMenuToggle }: HeaderProps) {
  const { username, clearAuth } = useAuthStore()
  const navigate = useNavigate()
  const { colorMode, toggleColorMode } = useColorMode()
  const [gatewayOk, setGatewayOk] = useState(false)
  const [gatewayVersion, setGatewayVersion] = useState('')

  useEffect(() => {
    const check = async () => {
      try {
        const h = await getGatewayHealth()
        setGatewayOk(h.status === 'ok')
        setGatewayVersion(h.version)
      } catch {
        setGatewayOk(false)
      }
    }
    check()
    const id = setInterval(check, 30_000)
    return () => clearInterval(id)
  }, [])

  const handleLogout = () => {
    signalRService.stop()
    clearAuth()
    navigate('/login')
  }

  return (
    <Flex
      as="header"
      h="48px"
      px="4"
      align="center"
      justify="space-between"
      bg="white"
      _dark={{ bg: 'gray.800' }}
      borderBottomWidth="1px"
      borderColor="gray.200"
      flexShrink={0}
    >
      {/* Brand */}
      <Flex align="center" gap="2" cursor="pointer" onClick={onMenuToggle}>
        <Cpu size={20} />
        <Text fontWeight="bold" fontSize="md">MicroClaw</Text>
      </Flex>

      {/* Right: status + user */}
      <Flex align="center" gap="4">
        {/* Gateway status */}
        <Flex align="center" gap="1">
          <Box
            w="8px"
            h="8px"
            borderRadius="full"
            bg={gatewayOk ? 'green.500' : 'red.500'}
          />
          <Text fontSize="xs" color="gray.500">
            {gatewayOk ? 'ok' : 'err'}
            {gatewayVersion && ` v${gatewayVersion}`}
          </Text>
        </Flex>

        {/* Color mode toggle */}
        <Box
          as="button"
          onClick={toggleColorMode}
          p="1"
          borderRadius="md"
          _hover={{ bg: 'gray.100', _dark: { bg: 'gray.700' } }}
          cursor="pointer"
          title={colorMode === 'dark' ? '切换到亮色模式' : '切换到暗色模式'}
        >
          {colorMode === 'dark' ? <Sun size={16} /> : <Moon size={16} />}
        </Box>

        {/* User + logout */}
        <Flex align="center" gap="2">
          <Text fontSize="sm" color="gray.700" _dark={{ color: 'gray.300' }}>
            {username}
          </Text>
          <Box
            as="button"
            onClick={handleLogout}
            p="1"
            borderRadius="md"
            _hover={{ bg: 'gray.100', _dark: { bg: 'gray.700' } }}
            cursor="pointer"
          >
            <LogOut size={16} />
          </Box>
        </Flex>
      </Flex>
    </Flex>
  )
}
