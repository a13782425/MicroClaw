import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '../auth'

describe('useAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('初始状态未登录', () => {
    const auth = useAuthStore()
    expect(auth.isLoggedIn).toBe(false)
    expect(auth.token).toBe('')
    expect(auth.username).toBe('')
  })

  it('setAuth 保存 token 和 username 并标记已登录', () => {
    const auth = useAuthStore()

    auth.setAuth('test-token', 'admin')

    expect(auth.isLoggedIn).toBe(true)
    expect(auth.token).toBe('test-token')
    expect(auth.username).toBe('admin')
    expect(localStorage.getItem('mc_token')).toBe('test-token')
    expect(localStorage.getItem('mc_username')).toBe('admin')
  })

  it('clearAuth 清除登录状态', () => {
    const auth = useAuthStore()
    auth.setAuth('tok', 'user1')

    auth.clearAuth()

    expect(auth.isLoggedIn).toBe(false)
    expect(auth.token).toBe('')
    expect(auth.username).toBe('')
    expect(localStorage.getItem('mc_token')).toBeNull()
    expect(localStorage.getItem('mc_username')).toBeNull()
  })

  it('从 localStorage 恢复登录状态', () => {
    localStorage.setItem('mc_token', 'persisted-token')
    localStorage.setItem('mc_username', 'persisted-user')

    // 重新创建 pinia 以重建 store
    setActivePinia(createPinia())
    const auth = useAuthStore()

    expect(auth.isLoggedIn).toBe(true)
    expect(auth.token).toBe('persisted-token')
    expect(auth.username).toBe('persisted-user')
  })
})
