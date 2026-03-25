import { useState } from 'react'
import { Box, Button, Field, Flex, Input, Text, VStack } from '@chakra-ui/react'
import { useNavigate } from 'react-router-dom'
import { login as loginApi } from '@/api/gateway'
import { useAuthStore } from '@/store/authStore'
import { toaster } from '@/components/ui/toaster'
import { signalRService } from '@/services/signalr'
import { Cpu } from 'lucide-react'

export default function LoginPage() {
  const navigate = useNavigate()
  const { setAuth } = useAuthStore()
  const [username, setUsername] = useState('admin')
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(false)

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!username || !password) {
      toaster.create({ type: 'warning', title: '请输入用户名和密码' })
      return
    }
    setLoading(true)
    try {
      const res = await loginApi(username, password)
      setAuth(res.token, res.username)
      signalRService.start()
      navigate('/sessions')
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : '登录失败'
      toaster.create({ type: 'error', title: msg })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Flex h="100vh" align="center" justify="center" bg="gray.50" _dark={{ bg: 'gray.900' }}>
      <Box
        bg="white"
        _dark={{ bg: 'gray.800' }}
        p="8"
        borderRadius="xl"
        boxShadow="lg"
        w="full"
        maxW="360px"
      >
        {/* Logo */}
        <Flex align="center" justify="center" gap="2" mb="6">
          <Cpu size={28} />
          <Text fontSize="xl" fontWeight="bold">MicroClaw</Text>
        </Flex>

        <form onSubmit={handleLogin}>
          <VStack gap="4">
            <Field.Root w="full">
              <Field.Label>用户名</Field.Label>
              <Input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="admin"
                autoComplete="username"
              />
            </Field.Root>
            <Field.Root w="full">
              <Field.Label>密码</Field.Label>
              <Input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="请输入密码"
                autoComplete="current-password"
              />
            </Field.Root>
            <Button
              type="submit"
              colorPalette="blue"
              w="full"
              loading={loading}
              loadingText="登录中…"
            >
              登录
            </Button>
          </VStack>
        </form>
      </Box>
    </Flex>
  )
}
