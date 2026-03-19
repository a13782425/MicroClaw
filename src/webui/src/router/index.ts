import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const LoginPage     = () => import('@/views/LoginPage.vue')
const SessionsPage  = () => import('@/views/SessionsPage.vue')
const CronPage      = () => import('@/views/CronPage.vue')
const ModelsPage    = () => import('@/views/ModelsPage.vue')
const ChannelsPage  = () => import('@/views/ChannelsPage.vue')
const ApprovalsPage = () => import('@/views/ApprovalsPage.vue')
const AgentsPage    = () => import('@/views/AgentsPage.vue')

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/sessions' },
    { path: '/login', name: 'login', component: LoginPage },
    { path: '/sessions', name: 'sessions', component: SessionsPage },
    { path: '/cron', name: 'cron', component: CronPage },
    { path: '/models', name: 'models', component: ModelsPage },
    { path: '/channels', name: 'channels', component: ChannelsPage },
    { path: '/approvals', name: 'approvals', component: ApprovalsPage },
    { path: '/agents', name: 'agents', component: AgentsPage },
  ]
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.name !== 'login' && !auth.isLoggedIn) return { name: 'login' }
  if (to.name === 'login' && auth.isLoggedIn) return { name: 'sessions' }
})
