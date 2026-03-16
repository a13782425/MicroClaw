import axios from 'axios'
import { useAuthStore } from '@/stores/auth'
import { router } from '@/router'

export type GatewayHealth = {
  status: string
  service: string
  utcNow: string
  version: string
}

axios.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.token) {
    config.headers.Authorization = 'Bearer ' + auth.token
  }
  return config
})

axios.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore().clearAuth()
      router.push({ name: 'login' })
    }
    return Promise.reject(error)
  }
)

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await axios.get<GatewayHealth>('/api/health')
  return data
}

export async function login(username: string, password: string) {
  const { data } = await axios.post('/api/auth/login', { username, password })
  return data as {
    token: string
    username: string
    role: string
    expiresAtUtc: string
  }
}