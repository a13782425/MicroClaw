import { useState, useEffect } from 'react'
import { Box, Flex, Text } from '@chakra-ui/react'
import { Cpu, LogOut } from 'lucide-react'
import { useAuthStore } from '@/store/authStore'
import { signalRService } from '@/services/signalr'
import { getGatewayHealth } from '@/api/gateway'
import { useNavigate } from 'react-router-dom'
import { ThemePicker } from '@/components/ui/ThemePicker'

interface HeaderProps {
  onMenuToggle: () => void
}

export default function Header({ onMenuToggle }: HeaderProps) {
  const { username, clearAuth } = useAuthStore()
  const navigate = useNavigate()
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
      bg="var(--mc-card)"
      borderBottomWidth="1px"
      borderColor="var(--mc-border)"
      flexShrink={0}
    >
      {/* Brand */}
      <Flex align="center" gap="2" cursor="pointer" onClick={onMenuToggle}>
        <Cpu size={20} color="var(--mc-primary)" />
        <Text fontWeight="bold" fontSize="md" color="var(--mc-text)">MicroClaw</Text>
      </Flex>

      {/* Right: status + user */}
      <Flex align="center" gap="4">
        {/* Gateway status */}
        <Flex align="center" gap="1">
          <Box
            w="8px"
            h="8px"
            borderRadius="full"
            bg={gatewayOk ? 'var(--mc-success)' : 'var(--mc-danger)'}
          />
          <Text fontSize="xs" color="var(--mc-text-muted)">
            {gatewayOk ? 'ok' : 'err'}
            {gatewayVersion && ` v${gatewayVersion}`}
          </Text>
        </Flex>

        {/* Theme picker */}
        <ThemePicker />

        {/* User + logout */}
        <Flex align="center" gap="2">
          <Text fontSize="sm" color="var(--mc-text)">
            {username}
          </Text>
          <Box
            as="button"
            onClick={handleLogout}
            p="1"
            borderRadius="md"
            _hover={{ bg: 'var(--mc-card-hover, var(--mc-card))' }}
            cursor="pointer"
            color="var(--mc-text)"
          >
            <LogOut size={16} />
          </Box>
        </Flex>
      </Flex>
    </Flex>
  )
}
