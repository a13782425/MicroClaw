import { ref, computed } from 'vue'
import { defineStore } from 'pinia'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem('mc_token') ?? '')
  const username = ref(localStorage.getItem('mc_username') ?? '')

  const isLoggedIn = computed(() => !!token.value)

  function setAuth(t: string, u: string) {
    token.value = t
    username.value = u
    localStorage.setItem('mc_token', t)
    localStorage.setItem('mc_username', u)
  }

  function clearAuth() {
    token.value = ''
    username.value = ''
    localStorage.removeItem('mc_token')
    localStorage.removeItem('mc_username')
  }

  return { token, username, isLoggedIn, setAuth, clearAuth }
})
