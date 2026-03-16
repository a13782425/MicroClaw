import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import DashboardPage from '@/views/DashboardPage.vue'
import LoginPage from '@/views/LoginPage.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'dashboard', component: DashboardPage },
    { path: '/login', name: 'login', component: LoginPage }
  ]
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.name !== 'login' && !auth.isLoggedIn) return { name: 'login' }
  if (to.name === 'login' && auth.isLoggedIn) return { name: 'dashboard' }
})
