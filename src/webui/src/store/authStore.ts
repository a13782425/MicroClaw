import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string
  username: string
  isLoggedIn: boolean
  setAuth: (token: string, username: string) => void
  clearAuth: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: '',
      username: '',
      isLoggedIn: false,
      setAuth: (token, username) =>
        set({ token, username, isLoggedIn: !!token }),
      clearAuth: () =>
        set({ token: '', username: '', isLoggedIn: false }),
    }),
    {
      name: 'mc-auth',
      partialize: (state) => ({
        token: state.token,
        username: state.username,
        isLoggedIn: state.isLoggedIn,
      }),
    },
  ),
)
