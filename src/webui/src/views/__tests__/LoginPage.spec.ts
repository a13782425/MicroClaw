import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import LoginPage from '../LoginPage.vue'
import { useAuthStore } from '@/stores/auth'

const pushMock = vi.fn()

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: pushMock }),
}))

vi.mock('@/services/gatewayApi', () => ({
  login: vi.fn(),
}))

// 获取 mock 的 login 函数以在各测试中控制行为
async function getLoginMock() {
  const { login } = await import('@/services/gatewayApi')
  return login as ReturnType<typeof vi.fn>
}

describe('LoginPage', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    pushMock.mockClear()
  })

  it('渲染登录表单', () => {
    const wrapper = mount(LoginPage, {
      global: {
        stubs: {
          ElIcon: true,
          ElCard: { template: '<div><slot /><slot name="header" /></div>' },
          ElForm: { template: '<form @submit.prevent><slot /></form>' },
          ElFormItem: { template: '<div><slot /></div>' },
          ElInput: { template: '<input />', props: ['modelValue'] },
          ElButton: { template: '<button><slot /></button>' },
        },
      },
    })
    expect(wrapper.text()).toContain('MicroClaw')
    expect(wrapper.text()).toContain('AI Agent 控制面板')
  })

  it('登录成功后跳转到 sessions 路由', async () => {
    const loginMock = await getLoginMock()
    loginMock.mockResolvedValueOnce({ token: 'jwt-123', username: 'admin' })

    const auth = useAuthStore()

    const wrapper = mount(LoginPage, {
      global: {
        stubs: {
          ElIcon: true,
          ElCard: { template: '<div><slot /><slot name="header" /></div>' },
          ElForm: {
            template: '<form @submit.prevent><slot /></form>',
            methods: { validate: () => Promise.resolve(true) },
          },
          ElFormItem: { template: '<div><slot /></div>' },
          ElInput: { template: '<input />', props: ['modelValue'] },
          ElButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
        },
      },
    })

    // 直接调用组件的 submit 逻辑
    await (wrapper.vm as any).submit()
    await flushPromises()

    expect(auth.isLoggedIn).toBe(true)
    expect(auth.token).toBe('jwt-123')
    expect(pushMock).toHaveBeenCalledWith({ name: 'sessions' })
  })

  it('登录失败不改变认证状态', async () => {
    const loginMock = await getLoginMock()
    loginMock.mockRejectedValueOnce({ response: { status: 401 } })

    const auth = useAuthStore()

    const wrapper = mount(LoginPage, {
      global: {
        stubs: {
          ElIcon: true,
          ElCard: { template: '<div><slot /><slot name="header" /></div>' },
          ElForm: {
            template: '<form @submit.prevent><slot /></form>',
            methods: { validate: () => Promise.resolve(true) },
          },
          ElFormItem: { template: '<div><slot /></div>' },
          ElInput: { template: '<input />', props: ['modelValue'] },
          ElButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
        },
      },
    })

    await (wrapper.vm as any).submit()
    await flushPromises()

    expect(auth.isLoggedIn).toBe(false)
    expect(pushMock).not.toHaveBeenCalled()
  })
})
