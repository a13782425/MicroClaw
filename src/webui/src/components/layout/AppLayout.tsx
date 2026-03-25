import { useEffect } from 'react'
import { Outlet } from 'react-router-dom'
import { Box, Flex } from '@chakra-ui/react'
import { useAuthStore } from '@/store/authStore'
import { useUIStore } from '@/store/uiStore'
import { signalRService } from '@/services/signalr'
import Header from './Header'
import Sidebar from './Sidebar'

export default function AppLayout() {
  const isLoggedIn = useAuthStore((s) => s.isLoggedIn)
  const { toggleSidebar } = useUIStore()

  // Start/stop SignalR based on login state
  useEffect(() => {
    if (!isLoggedIn) return
    signalRService.start()
    return () => { signalRService.stop() }
  }, [isLoggedIn])

  return (
    <Flex direction="column" h="100vh">
      <Header onMenuToggle={toggleSidebar} />
      <Flex flex="1" overflow="hidden">
        <Sidebar />
        <Box flex="1" overflow="auto">
          <Outlet />
        </Box>
      </Flex>
    </Flex>
  )
}
