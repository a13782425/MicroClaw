import axios from 'axios'
import { useAuthStore } from '@/store/authStore'

type ErrorResponsePayload = {
  message?: string
  detail?: string
  error?: string
  title?: string
}

const instance = axios.create({
  baseURL: '/',
  timeout: 30000,
})

instance.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

instance.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore.getState().clearAuth()
      window.location.href = '/login'
    }
    return Promise.reject(error)
  },
)

export function getApiErrorMessage(error: unknown, fallback = '请求失败'): string {
  if (axios.isAxiosError<ErrorResponsePayload>(error)) {
    const payload = error.response?.data as ErrorResponsePayload | string | undefined
    if (typeof payload === 'string' && payload.trim()) {
      return payload.trim()
    }

    if (payload && typeof payload === 'object') {
      const detail = payload.message ?? payload.detail ?? payload.error ?? payload.title
      if (detail && detail.trim()) {
        return detail.trim()
      }
    }

    if (error.message?.trim() && !/^Request failed with status code \d+$/i.test(error.message.trim())) {
      return error.message.trim()
    }

    return fallback
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message.trim()
  }

  if (typeof error === 'string' && error.trim()) {
    return error.trim()
  }

  return fallback
}

export default instance
